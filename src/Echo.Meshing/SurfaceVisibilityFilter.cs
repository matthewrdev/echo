using Echo.Contracts;
using Echo.Geometry;

namespace Echo.Meshing;

public static class SurfaceVisibilityFilter
{
    public static Surface Filter(Surface surface, ProjectData project, ReconstructionScene scene,
        IReadOnlyList<DepthMap> maps, CancellationToken cancellation)
    {
        return Filter(surface, MeshDepthViews.Build(project, scene, maps), project.Settings.CloudDepthTolerance, cancellation);
    }

    public static Surface Filter(Surface surface, IReadOnlyList<DepthView> views,
        double relativeTolerance, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!(relativeTolerance > 0) || !double.IsFinite(relativeTolerance))
        {
            throw new ArgumentOutOfRangeException(nameof(relativeTolerance));
        }
        var rejected = new bool[surface.Faces.Count];
        Parallel.For(0, surface.Faces.Count, new ParallelOptions
        {
            CancellationToken = cancellation,
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2)
        }, index =>
        {
            var face = surface.Faces[index];
            var centre = (new V3(surface.Vertices[face[0]]) + new V3(surface.Vertices[face[1]])
                + new V3(surface.Vertices[face[2]])) / 3;
            var supporting = 0.0;
            var contradicted = 0.0;
            var freeSpace = 0;
            foreach (var view in views)
            {
                var observation = view.Observe(centre, relativeTolerance);
                if (!observation.IsValid)
                {
                    continue;
                }

                var tolerance = Math.Max(observation.CameraDepth * relativeTolerance * 2,
                    observation.CameraDepth / view.Focal * 4);
                // A distant, uncertain observation must not outvote a closer
                // measurement simply because that flight pass has more photos.
                var precision = 1 / (tolerance * tolerance);
                if (Math.Abs(observation.Distance) <= tolerance)
                {
                    supporting += precision;
                }
                else if (observation.HasFullCoverage && observation.Distance > tolerance)
                {
                    freeSpace++;
                    contradicted += precision;
                }
            }

            // Unknown and occluded space are deliberately neutral. Reject an
            // inferred sheet only when multiple visible rays contradict it and
            // carry more precision than observations supporting the surface.
            rejected[index] = freeSpace >= 3 && contradicted > supporting;
        });

        var result = new Surface();
        var vertices = new Dictionary<int, int>();
        var assigned = surface.SourceImages.Count == surface.Faces.Count;
        for (var index = 0; index < surface.Faces.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (rejected[index])
            {
                continue;
            }

            var face = surface.Faces[index];
            var output = new int[3];
            for (var corner = 0; corner < 3; corner++)
            {
                var vertex = face[corner];
                if (!vertices.TryGetValue(vertex, out var mapped))
                {
                    mapped = result.Vertices.Count;
                    result.Vertices.Add(surface.Vertices[vertex]);
                    vertices.Add(vertex, mapped);
                }
                output[corner] = mapped;
            }
            result.Faces.Add(output);
            if (assigned)
            {
                result.SourceImages.Add(surface.SourceImages[index]);
            }
        }
        return result;
    }
}
