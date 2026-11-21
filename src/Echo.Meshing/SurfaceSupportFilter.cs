using Echo.Models;
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
        using var scratch = work?.CreateScope();
        using var index = new SurfaceTriangleIndex(observed, cancellation, work);
        var reader = new SurfaceGeometryReader(reconstruction);
        IList<bool> supported = scratch?.List(new BoolRecordCodec()) ?? new List<bool>(reconstruction.Vertices.Count);
        var points = new V3[4096];
        var accepted = new bool[points.Length];
        var options = new ParallelOptions
        {
            CancellationToken = cancellation,
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2)
        };
        for (var start = 0; start < reconstruction.Vertices.Count; start += points.Length)
        {
            cancellation.ThrowIfCancellationRequested();
            var count = Math.Min(points.Length, reconstruction.Vertices.Count - start);
            reader.ReadVertices(start, points.AsSpan(0, count));
            Parallel.For(0, count, options, vertex =>
                accepted[vertex] = index.Nearest(points[vertex], maximumDistance).Normal.Length > .5);
            if (supported is PagedList<bool> paged)
            {
                paged.AddRange(accepted.AsSpan(0, count));
            }
            else
            {
                for (var vertex = 0; vertex < count; vertex++)
                {
                    supported.Add(accepted[vertex]);
                }
            }
            progress?.Invoke(new("Surface support", "Checking completion against the observed surface.", start + count, reconstruction.Vertices.Count, "vertices"));
        }
        var result = work?.Surface() ?? new Surface();
        var writer = new SurfaceGeometryWriter(result);
        Span<int> output = stackalloc int[3];
        IList<int> lookup = scratch?.Filled(reconstruction.Vertices.Count, -1, new IntRecordCodec(), cancellation) ?? Enumerable.Repeat(-1, reconstruction.Vertices.Count).ToArray();
        for (var faceIndex = 0; faceIndex < reconstruction.Faces.Count; faceIndex++)
        {
            cancellation.ThrowIfCancellationRequested();
            var face = reader.Face(faceIndex);
            var a = reader.Vertex(face.A);
            var normal = (reader.Vertex(face.B) - a).Cross(reader.Vertex(face.C) - a).Unit;
            var validNormal = normal.Length > .5;
            var nearSurface = supported[face[0]] && supported[face[1]] && supported[face[2]];
            if (!validNormal || !nearSurface)
            {
                continue;
            }

            for (var corner = 0; corner < 3; corner++)
            {
                var vertex = face[corner];
                var mapped = lookup[vertex];
                if (mapped < 0)
                {
                    mapped = result.Vertices.Count;
                    writer.AddVertex(reader.Vertex(vertex));
                    lookup[vertex] = mapped;
                }
                output[corner] = mapped;
            }
            writer.AddFace(new SurfaceTriangleVertices(output[0], output[1], output[2]));
        }
        return result;
    }
}
