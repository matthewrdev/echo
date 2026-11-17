using Echo.Contracts;
using Echo.Geometry;
using Echo.Storage;

namespace Echo.Simplification;

internal static class SimplificationGeometry
{
    public static IEnumerable<V3> Vertices(Surface mesh)
    {
        if (mesh.Vertices is not PagedList<double[]> paged)
        {
            foreach (var vertex in mesh.Vertices)
            {
                yield return new V3(vertex);
            }
            yield break;
        }
        var values = new V3[4096];
        var codec = new PositionRecordCodec();
        for (var start = 0; start < paged.Count; start += values.Length)
        {
            var count = Math.Min(values.Length, paged.Count - start);
            paged.ReadRange(start, values.AsSpan(0, count), codec);
            for (var index = 0; index < count; index++)
            {
                yield return values[index];
            }
        }
    }

    public static IEnumerable<FaceVertices> Faces(Surface mesh)
    {
        if (mesh.Faces is not PagedList<int[]> paged)
        {
            foreach (var face in mesh.Faces)
            {
                yield return new FaceVertices(face[0], face[1], face[2]);
            }
            yield break;
        }
        var values = new FaceVertices[4096];
        var codec = new FaceVerticesCodec();
        for (var start = 0; start < paged.Count; start += values.Length)
        {
            var count = Math.Min(values.Length, paged.Count - start);
            paged.ReadRange(start, values.AsSpan(0, count), codec);
            for (var index = 0; index < count; index++)
            {
                yield return values[index];
            }
        }
    }

    public static Surface Copy(Surface source, WorkBuffers? work, CancellationToken cancellation)
    {
        var result = work?.Surface() ?? new Surface();
        var vertices = result.Vertices as PagedList<double[]>;
        var faces = result.Faces as PagedList<int[]>;
        var positionCodec = new PositionRecordCodec();
        var faceCodec = new FaceVerticesCodec();
        foreach (var point in Vertices(source))
        {
            cancellation.ThrowIfCancellationRequested();
            if (vertices == null)
            {
                result.Vertices.Add([point.X, point.Y, point.Z]);
            }
            else
            {
                vertices.AddRecord(point, positionCodec);
            }
        }
        foreach (var face in Faces(source))
        {
            cancellation.ThrowIfCancellationRequested();
            if (faces == null)
            {
                result.Faces.Add([face.A, face.B, face.C]);
            }
            else
            {
                faces.AddRecord(face, faceCodec);
            }
        }
        return result;
    }
}
