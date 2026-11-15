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
            var triangle = new SurfaceTriangle(new V3(surface.Vertices[face[0]]),
                new V3(surface.Vertices[face[1]]), new V3(surface.Vertices[face[2]]));
            rejected[index] = SurfaceVisibilityEvidence.Contradicts(triangle, views, relativeTolerance);
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
