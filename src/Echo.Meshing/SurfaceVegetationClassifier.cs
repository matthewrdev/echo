using System.Buffers;
using Echo.Contracts;
using Echo.Compute;
using Echo.Geometry;
using Echo.SceneSemantics;
using Echo.Storage;

namespace Echo.Meshing;

public static class SurfaceVegetationClassifier
{
    public static IReadOnlyList<bool> Classify(Surface surface, ProjectData project, ReconstructionScene scene,
        IReadOnlyList<DepthMap> maps, IReadOnlyDictionary<string, SemanticMap> semantics, CancellationToken cancellation, WorkBuffers? work = null, Action<OperationProgress>? progress = null, IComputeService? compute = null)
    {
        var photos = project.Images.ToDictionary(photo => photo.Id);
        var cameras = scene.Cameras.ToDictionary(camera => camera.ImageId);
        var views = maps.Where(map => semantics.ContainsKey(map.ImageId))
            .Select(map => new DepthView(photos[map.ImageId], cameras[map.ImageId], map)).ToArray();
        using var visibility = new SurfaceTriangleIndex(surface, cancellation);
        IList<V3> normals = work?.Filled(surface.Vertices.Count, new V3(), new V3RecordCodec()) ?? new V3[surface.Vertices.Count];
        foreach (var face in surface.Faces)
        {
            var a = new V3(surface.Vertices[face[0]]);
            var b = new V3(surface.Vertices[face[1]]);
            var c = new V3(surface.Vertices[face[2]]);
            var normal = (b - a).Cross(c - a);
            foreach (var vertex in face)
            {
                normals[vertex] += normal;
            }
        }
        IList<bool> result = work?.Filled(surface.Vertices.Count, false, new BoolRecordCodec()) ?? new bool[surface.Vertices.Count];
        var origin = views.Length == 0 ? default : views[0].Centre;
        using var acceleration = SemanticProjectionCompute.Open(compute, project, views, photos, semantics, origin, cancellation);
        var projectionBuffer = acceleration == null ? null : ArrayPool<float>.Shared.Rent(checked(1024 * views.Length * 4));
        try
        {
            var failed = false;
            for (var start = 0; start < result.Count; start += 1024)
            {
                var end = Math.Min(result.Count, start + 1024);
                float[]? projectedVotes = null;
                if (acceleration != null && !failed)
                {
                    try
                    {
                        SemanticProjectionCompute.Evaluate(acceleration, surface, normals, start, end - start, origin, views.Length, projectionBuffer);
                        projectedVotes = projectionBuffer;
                    }
                    catch (ComputeException) when (project.Settings.ComputeBackend == ComputeBackend.Auto)
                    {
                        failed = true;
                    }
                }
                Parallel.For(start, end, new ParallelOptions { CancellationToken = cancellation, MaxDegreeOfParallelism = Math.Min(8, Environment.ProcessorCount) }, index =>
            {
                var point = new V3(surface.Vertices[index]);
                Span<VegetationViewVote> votes = stackalloc VegetationViewVote[8];
                var count = 0;
                for (var viewIndex = 0; viewIndex < views.Length; viewIndex++)
                {
                    var view = views[viewIndex];
                    if (projectedVotes != null)
                    {
                        var entry = ((index - start) * views.Length + viewIndex) * 4;
                        if (projectedVotes[entry + 3] == 0)
                        {
                            continue;
                        }
                        var acceleratedWeakest = 0;
                        for (var vote = 1; vote < count; vote++)
                        {
                            if (votes[vote].Weight < votes[acceleratedWeakest].Weight)
                            {
                                acceleratedWeakest = vote;
                            }
                        }
                        var acceleratedWeight = projectedVotes[entry];
                        if (count == votes.Length && acceleratedWeight <= votes[acceleratedWeakest].Weight)
                        {
                            continue;
                        }
                        if (visibility.HasOccluder(view.Centre, point))
                        {
                            continue;
                        }
                        var acceleratedSlot = count < votes.Length ? count++ : acceleratedWeakest;
                        votes[acceleratedSlot] = new VegetationViewVote(acceleratedWeight, projectedVotes[entry + 1], projectedVotes[entry + 2]);
                        continue;
                    }
                    var projection = view.Projection.Project(point);
                    if (!projection.HasValue)
                    {
                        continue;
                    }
                    var photo = photos[view.Map.ImageId];
                    var map = semantics[photo.Id];
                    var u = projection.Value.X / photo.Width;
                    var v = projection.Value.Y / photo.Height;
                    var inside = u > 0 && v > 0 && u < 1 && v < 1;
                    if (!inside)
                    {
                        continue;
                    }
                    var direction = view.Centre - point;
                    var facing = normals[index].Length > 0 ? Math.Abs(normals[index].Unit.Dot(direction.Unit)) : 1;
                    var weight = facing * view.Focal / Math.Max(1e-8, direction.Length);
                    var weakest = 0;
                    for (var vote = 1; vote < count; vote++)
                    {
                        if (votes[vote].Weight < votes[weakest].Weight)
                        {
                            weakest = vote;
                        }
                    }
                    if (facing < .05 || (count == votes.Length && weight <= votes[weakest].Weight))
                    {
                        continue;
                    }
                    var measured = view.TrySurfacePoint(point, .01, out _);
                    var missingDepth = surface.Faces.Count > 0 && !double.IsFinite(view.Difference(point));
                    if ((!measured && !missingDepth) || visibility.HasOccluder(view.Centre, point))
                    {
                        continue;
                    }
                    // Completed vegetation may have no measured depth. Its existing
                    // mesh line of sight can establish semantic visibility; it never
                    // provides new geometry or overrides contradictory measured depth.
                    var vegetation = SemanticProbabilities.ConservativeSample(map, map.VegetationSamples, u, v);
                    var rock = SemanticProbabilities.ConservativeSample(map, map.RockSamples, u, v);
                    var slot = count < votes.Length ? count++ : weakest;
                    votes[slot] = new VegetationViewVote(weight, vegetation, rock);
                }
                double total = 0;
                double plant = 0;
                double stone = 0;
                var confirmed = 0;
                foreach (var vote in votes[..count])
                {
                    total += vote.Weight;
                    plant += vote.Weight * vote.Vegetation;
                    stone += vote.Weight * vote.Rock;
                    if (vote.Vegetation >= .75)
                    {
                        confirmed++;
                    }
                }
                result[index] = confirmed >= 2 && plant >= total * .65 && stone < total * .2;
            });
                progress?.Invoke(new("Vegetation classification", "Combining vegetation evidence from visible photographs.", end, result.Count, "vertices"));
            }
        }
        finally
        {
            if (projectionBuffer != null)
            {
                ArrayPool<float>.Shared.Return(projectionBuffer);
            }
        }
        return (IReadOnlyList<bool>)result;
    }

    public static IReadOnlyList<double> Tolerances(Surface surface, IReadOnlyList<bool> vegetation, double tolerance, WorkBuffers? work = null, CancellationToken cancellation = default)
    {
        if (vegetation.Count != surface.Vertices.Count)
        {
            throw new ArgumentException("Expected one classification per vertex.", nameof(vegetation));
        }
        IList<double> result = work?.List(new DoubleRecordCodec()) ?? new List<double>(vegetation.Count);
        foreach (var value in vegetation)
        {
            cancellation.ThrowIfCancellationRequested();
            result.Add(value ? tolerance : 0);
        }
        foreach (var face in surface.Faces)
        {
            if (face.Any(vertex => !vegetation[vertex]))
            {
                // Lock both sides of the material transition: a collapse must
                // never drag a rock vertex into a lower-detail vegetation region.
                foreach (var vertex in face)
                {
                    result[vertex] = 0;
                }
            }
        }
        var neighbours = SurfaceTopologyBuilder.Build(surface, cancellation, work).Neighbours;
        for (var face = 0; face < surface.Faces.Count; face++)
        {
            for (var edge = 0; edge < 3; edge++)
            {
                if (neighbours[face * 3 + edge] < 0)
                {
                    result[surface.Faces[face][edge]] = 0;
                    result[surface.Faces[face][(edge + 1) % 3]] = 0;
                }
            }
        }
        return (IReadOnlyList<double>)result;
    }
}
