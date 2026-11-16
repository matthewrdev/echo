using Echo.Contracts;
using Echo.Geometry;
using Echo.Storage;

namespace Echo.Meshing;

public static class SurfaceSupportFilter
{
    public static bool HasGaps(Surface surface, CancellationToken cancellation, WorkBuffers? work = null)
    {
        var loops = SurfaceBoundaryLoops.Find(surface, cancellation, work);
        var count = 0;
        foreach (var loop in loops)
        {
            if (++count > 1)
            {
                return true;
            }
            var origin = new V3(surface.Vertices[loop.Vertices[0]]);
            var area = new V3();
            for (var index = 0; index < loop.Vertices.Count; index++)
            {
                var point = new V3(surface.Vertices[loop.Vertices[index]]);
                var next = new V3(surface.Vertices[loop.Vertices[(index + 1) % loop.Vertices.Count]]);
                area += (next - origin).Cross(point - origin);
            }
            if (area.Dot(loop.AdjacentNormal) > 1e-12)
            {
                return true;
            }
        }
        return false;
    }

    public static Surface Filter(Surface reconstruction, Surface observed, double maximumDistance, CancellationToken cancellation, WorkBuffers? work = null, Action<OperationProgress>? progress = null)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!(maximumDistance > 0) || !double.IsFinite(maximumDistance))
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDistance));
        }
        using var index = new SurfaceTriangleIndex(observed, cancellation);
        IList<bool> supported = work?.Filled(reconstruction.Vertices.Count, false, new BoolRecordCodec()) ?? new bool[reconstruction.Vertices.Count];
        for (var start = 0; start < reconstruction.Vertices.Count; start += 4096)
        {
            var end = Math.Min(reconstruction.Vertices.Count, start + 4096);
            Parallel.For(start, end, new ParallelOptions
            {
                CancellationToken = cancellation,
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2)
            }, vertex => supported[vertex] = index.Nearest(new V3(reconstruction.Vertices[vertex]), maximumDistance).Normal.Length > .5);
            progress?.Invoke(new("Surface support", "Checking completion against the observed surface.", end, reconstruction.Vertices.Count, "vertices"));
        }
        var result = work?.Surface() ?? new Surface();
        var lookup = work?.Map(new IntRecordCodec(), new IntRecordCodec()) ?? new Dictionary<int, int>();
        foreach (var face in reconstruction.Faces)
        {
            cancellation.ThrowIfCancellationRequested();
            var a = new V3(reconstruction.Vertices[face[0]]);
            var normal = (new V3(reconstruction.Vertices[face[1]]) - a).Cross(new V3(reconstruction.Vertices[face[2]]) - a).Unit;
            var validNormal = normal.Length > .5;
            var nearSurface = face.All(vertex => supported[vertex]);
            if (!validNormal || !nearSurface)
            {
                continue;
            }
            var output = new int[3];
            for (var corner = 0; corner < 3; corner++)
            {
                var vertex = face[corner];
                if (!lookup.TryGetValue(vertex, out var mapped))
                {
                    mapped = result.Vertices.Count;
                    result.Vertices.Add(reconstruction.Vertices[vertex]);
                    lookup.Add(vertex, mapped);
                }
                output[corner] = mapped;
            }
            result.Faces.Add(output);
        }
        return result;
    }
}
