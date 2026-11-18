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
        IReadOnlyList<DepthMap> maps, IReadOnlyDictionary<string, SemanticMap> semantics, CancellationToken cancellation, WorkBuffers? work = null, Action<OperationProgress>? progress = null, IComputeService? compute = null, MeshDepthContext? depthContext = null)
    {
        depthContext ??= new MeshDepthContext(project, scene, maps);
        var photos = depthContext.Photos;
        var views = depthContext.Views.Where(view => semantics.ContainsKey(view.Map.ImageId)).ToArray();
        using var visibility = new SurfaceTriangleIndex(surface, cancellation, work);
        var reader = new SurfaceGeometryReader(surface);
        using var scratch = work?.CreateScope(preferResident: true);
        IList<V3> normals = scratch?.Filled(surface.Vertices.Count, new V3(), new V3RecordCodec()) ?? new V3[surface.Vertices.Count];
        for (var faceIndex = 0; faceIndex < surface.Faces.Count; faceIndex++)
        {
            cancellation.ThrowIfCancellationRequested();
            var face = reader.Face(faceIndex);
            var a = reader.Vertex(face.A);
            var b = reader.Vertex(face.B);
            var c = reader.Vertex(face.C);
            var normal = (b - a).Cross(c - a);
            for (var corner = 0; corner < 3; corner++)
            {
                normals[face[corner]] += normal;
            }
        }
        IList<bool> result = work?.Filled(surface.Vertices.Count, false, new BoolRecordCodec()) ?? new bool[surface.Vertices.Count];
        var origin = views.Length == 0 ? default : views[0].Centre;
        using var acceleration = SemanticProjectionCompute.Open(compute, project, views, photos, semantics, origin, cancellation);
        using var occlusion = acceleration == null ? null : SurfaceOcclusionPreparation.Open(compute,
            project.Settings.ComputeBackend, visibility, cancellation, acceleration);
        var rays = occlusion == null ? null : new SurfaceRay[checked(1024 * views.Length)];
        var raySlots = rays == null ? null : new int[rays.Length];
        var hidden = rays == null ? null : new bool[rays.Length];
        var projectionBuffer = acceleration == null ? null : ArrayPool<float>.Shared.Rent(checked(1024 * views.Length * 4));
        var points = new V3[1024];
        var batchNormals = new V3[1024];
        var labels = new bool[1024];
        try
        {
            var failed = false;
            for (var start = 0; start < result.Count; start += 1024)
            {
                var end = Math.Min(result.Count, start + 1024);
                var batchCount = end - start;
                reader.ReadVertices(start, points.AsSpan(0, batchCount));
                RecordListOperations.Read(normals, start, batchNormals.AsSpan(0, batchCount));
                float[]? projectedVotes = null;
                if (acceleration != null && !failed)
                {
                    try
                    {
                        SemanticProjectionCompute.Evaluate(acceleration, points.AsSpan(0, batchCount), batchNormals.AsSpan(0, batchCount),
                            surface.Faces.Count, origin, views.Length, projectionBuffer);
                        projectedVotes = projectionBuffer;
                    }
                    catch (ComputeException) when (project.Settings.ComputeBackend == ComputeBackend.Auto)
                    {
                        failed = true;
                    }
                }
                if (projectedVotes != null && occlusion != null)
                {
                    var count = 0;
                    for (var vertex = start; vertex < end; vertex++)
                    {
                        var point = points[vertex - start];
                        for (var view = 0; view < views.Length; view++)
                        {
                            var slot = ((vertex - start) * views.Length + view) * 4;
                            if (projectedVotes[slot + 3] != 0)
                            {
                                raySlots![count] = slot;
                                rays![count++] = new SurfaceRay(views[view].Centre, point);
                            }
                        }
                    }
                    occlusion.Evaluate(rays.AsSpan(0, count), hidden.AsSpan(0, count), cancellation);
                    for (var ray = 0; ray < count; ray++)
                    {
                        if (hidden![ray])
                        {
                            projectedVotes[raySlots![ray] + 3] = 0;
                        }
                    }
                }
                Parallel.For(start, end, new ParallelOptions { CancellationToken = cancellation, MaxDegreeOfParallelism = Math.Min(8, Environment.ProcessorCount) }, index =>
            {
                var point = points[index - start];
                var normal = batchNormals[index - start];
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
                        if (occlusion == null && visibility.HasOccluder(view.Centre, point))
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
                    var facing = normal.Length > 0 ? Math.Abs(normal.Unit.Dot(direction.Unit)) : 1;
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
                labels[index - start] = confirmed >= 2 && plant >= total * .65 && stone < total * .2;
            });
                RecordListOperations.Write(result, start, labels.AsSpan(0, batchCount));
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

    public static bool[] FaceLabels(Surface surface, IReadOnlyList<bool> vertices, CancellationToken cancellation)
    {
        var reader = new SurfaceGeometryReader(surface);
        var result = new bool[surface.Faces.Count];
        var batch = new SurfaceTriangleVertices[4096];
        for (var start = 0; start < result.Length; start += batch.Length)
        {
            cancellation.ThrowIfCancellationRequested();
            var count = Math.Min(batch.Length, result.Length - start);
            reader.ReadFaces(start, batch.AsSpan(0, count));
            for (var index = 0; index < count; index++)
            {
                var face = batch[index];
                result[start + index] = vertices[face.A] && vertices[face.B] && vertices[face.C];
            }
        }
        return result;
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
        var reader = new SurfaceGeometryReader(surface);
        for (var index = 0; index < surface.Faces.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var face = reader.Face(index);
            var boundary = !vegetation[face.A] || !vegetation[face.B] || !vegetation[face.C];
            if (boundary)
            {
                // Lock both sides of the material transition: a collapse must
                // never drag a rock vertex into a lower-detail vegetation region.
                for (var corner = 0; corner < 3; corner++)
                {
                    result[face[corner]] = 0;
                }
            }
        }
        using var scratch = work?.CreateScope();
        var neighbours = SurfaceTopologyBuilder.Build(surface, cancellation, scratch).Neighbours;
        for (var face = 0; face < surface.Faces.Count; face++)
        {
            cancellation.ThrowIfCancellationRequested();
            var vertices = reader.Face(face);
            for (var edge = 0; edge < 3; edge++)
            {
                if (neighbours[face * 3 + edge] < 0)
                {
                    result[vertices[edge]] = 0;
                    result[vertices[(edge + 1) % 3]] = 0;
                }
            }
        }
        return (IReadOnlyList<double>)result;
    }
}
