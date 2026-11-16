using Echo.Contracts;
using Echo.Geometry;
using Echo.Storage;

namespace Echo.Meshing;

public static class SurfaceVisibilityFilter
{
    public static Surface Filter(Surface surface, ProjectData project, ReconstructionScene scene,
        IReadOnlyList<DepthMap> maps, CancellationToken cancellation, WorkBuffers? work = null, Action<OperationProgress>? progress = null)
    {
        return Filter(surface, MeshDepthViews.Build(project, scene, maps), project.Settings.CloudDepthTolerance, cancellation, work, progress);
    }

    public static Surface Filter(Surface surface, IReadOnlyList<DepthView> views,
        double relativeTolerance, CancellationToken cancellation, WorkBuffers? work = null, Action<OperationProgress>? progress = null)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!(relativeTolerance > 0) || !double.IsFinite(relativeTolerance))
        {
            throw new ArgumentOutOfRangeException(nameof(relativeTolerance));
        }
        IList<bool> rejected = work?.Filled(surface.Faces.Count, false, new BoolRecordCodec()) ?? new bool[surface.Faces.Count];
        for (var start = 0; start < surface.Faces.Count; start += 4096)
        {
            var end = Math.Min(surface.Faces.Count, start + 4096);
            Parallel.For(start, end, new ParallelOptions
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
            progress?.Invoke(new("Visibility filtering", "Checking triangles against observed empty space.", end, surface.Faces.Count, "triangles"));
        }

        var result = work?.Surface() ?? new Surface();
        var vertices = work?.Map(new IntRecordCodec(), new IntRecordCodec()) ?? new Dictionary<int, int>();
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
