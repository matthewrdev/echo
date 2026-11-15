using Echo.Contracts;
using Echo.Geometry;
using Echo.SceneSemantics;

namespace Echo.Meshing;

public static class SurfaceVegetationClassifier
{
    public static bool[] Classify(Surface surface, ProjectData project, ReconstructionScene scene,
        IReadOnlyList<DepthMap> maps, IReadOnlyDictionary<string, SemanticMap> semantics, CancellationToken cancellation)
    {
        var photos = project.Images.ToDictionary(photo => photo.Id);
        var cameras = scene.Cameras.ToDictionary(camera => camera.ImageId);
        var views = maps.Where(map => semantics.ContainsKey(map.ImageId))
            .Select(map => new DepthView(photos[map.ImageId], cameras[map.ImageId], map)).ToArray();
        var visibility = new SurfaceTriangleIndex(surface, cancellation);
        var normals = new V3[surface.Vertices.Count];
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
        var result = new bool[surface.Vertices.Count];
        Parallel.For(0, result.Length, new ParallelOptions { CancellationToken = cancellation, MaxDegreeOfParallelism = Math.Min(8, Environment.ProcessorCount) }, index =>
        {
            var point = new V3(surface.Vertices[index]);
            Span<VegetationViewVote> votes = stackalloc VegetationViewVote[8];
            var count = 0;
            foreach (var view in views)
            {
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
                var vegetation = SemanticProbabilities.ConservativeSample(map, map.Vegetation, u, v);
                var rock = SemanticProbabilities.ConservativeSample(map, map.Rock, u, v);
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
        return result;
    }

    public static double[] Tolerances(Surface surface, IReadOnlyList<bool> vegetation, double tolerance)
    {
        if (vegetation.Count != surface.Vertices.Count)
        {
            throw new ArgumentException("Expected one classification per vertex.", nameof(vegetation));
        }
        var result = vegetation.Select(value => value ? tolerance : 0).ToArray();
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
        var neighbours = SurfaceTopologyBuilder.Build(surface, default).Neighbours;
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
        return result;
    }
}
