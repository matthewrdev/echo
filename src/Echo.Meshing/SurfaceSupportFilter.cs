using Echo.Contracts;
using Echo.Geometry;

namespace Echo.Meshing;

public static class SurfaceSupportFilter
{
    public static bool HasGaps(Surface surface, CancellationToken cancellation)
    {
        var loops = SurfaceBoundaryLoops.Find(surface, cancellation);
        if (loops.Count > 1)
        {
            return true;
        }
        foreach (var loop in loops)
        {
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

    public static Surface Filter(Surface reconstruction, Surface observed, double maximumDistance, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!(maximumDistance > 0) || !double.IsFinite(maximumDistance))
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDistance));
        }
        var index = new SurfaceTriangleIndex(observed, cancellation);
        var nearest = new SurfaceClosestPoint[reconstruction.Vertices.Count];
        var points = reconstruction.Vertices.Select(point => new V3(point)).ToArray();
        Parallel.For(0, points.Length, new ParallelOptions
        {
            CancellationToken = cancellation,
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2)
        }, vertex => nearest[vertex] = index.Nearest(points[vertex], maximumDistance));
        var result = new Surface();
        var lookup = new Dictionary<int, int>();
        foreach (var face in reconstruction.Faces)
        {
            cancellation.ThrowIfCancellationRequested();
            var normal = (points[face[1]] - points[face[0]]).Cross(points[face[2]] - points[face[0]]).Unit;
            var validNormal = normal.Length > .5;
            var nearSurface = face.All(vertex => nearest[vertex].Normal.Length > .5);
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
