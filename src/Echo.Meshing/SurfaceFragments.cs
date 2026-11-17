using Echo.Contracts;
using Echo.Storage;
using Echo.Geometry;

namespace Echo.Meshing;

internal static class SurfaceFragments
{
    public static Surface RemoveSmall(Surface source, double voxelSize, CancellationToken cancellation, WorkBuffers? work = null)
    {
        cancellation.ThrowIfCancellationRequested();
        using var scratch = work?.CreateScope();
        var reader = new SurfaceGeometryReader(source);
        IList<int> parents = scratch?.List(new IntRecordCodec()) ?? new List<int>(source.Vertices.Count);
        for (var index = 0; index < source.Vertices.Count; index++)
        {
            parents.Add(index);
        }
        for (var faceIndex = 0; faceIndex < source.Faces.Count; faceIndex++)
        {
            cancellation.ThrowIfCancellationRequested();
            var face = reader.Face(faceIndex);
            for (var corner = 1; corner < 3; corner++)
            {
                var a = Root(parents, face[0]);
                var b = Root(parents, face[corner]);
                parents[Math.Max(a, b)] = Math.Min(a, b);
            }
        }

        IList<int> counts = scratch?.Filled(parents.Count, 0, new IntRecordCodec()) ?? new int[parents.Count];
        IList<double> areas = scratch?.Filled(parents.Count, 0d, new DoubleRecordCodec()) ?? new double[parents.Count];
        var largest = 0;
        for (var faceIndex = 0; faceIndex < source.Faces.Count; faceIndex++)
        {
            cancellation.ThrowIfCancellationRequested();
            var face = reader.Face(faceIndex);
            var root = Root(parents, face[0]);
            var origin = reader.Vertex(face.A);
            areas[root] += (reader.Vertex(face.B) - origin).Cross(reader.Vertex(face.C) - origin).Length * .5;
            counts[root]++;
            if (areas[root] > areas[largest])
            {
                largest = root;
            }
        }

        var result = work?.Surface() ?? new Surface();
        var writer = new SurfaceGeometryWriter(result);
        Span<int> output = stackalloc int[3];
        IList<int> remap = scratch?.Filled(source.Vertices.Count, -1, new IntRecordCodec()) ?? Enumerable.Repeat(-1, source.Vertices.Count).ToArray();
        for (var faceIndex = 0; faceIndex < source.Faces.Count; faceIndex++)
        {
            cancellation.ThrowIfCancellationRequested();
            var face = reader.Face(faceIndex);
            var root = Root(parents, face[0]);
            var smallFragment = root != largest && counts[root] <= 64 && areas[root] < voxelSize * voxelSize * 4;
            if (smallFragment)
            {
                continue;
            }

            for (var corner = 0; corner < 3; corner++)
            {
                var vertex = face[corner];
                var index = remap[vertex];
                if (index < 0)
                {
                    index = result.Vertices.Count;
                    remap[vertex] = index;
                    writer.AddVertex(reader.Vertex(vertex));
                }
                output[corner] = index;
            }
            writer.AddFace(new SurfaceTriangleVertices(output[0], output[1], output[2]));
        }
        return result;
    }

    private static int Root(IList<int> parents, int vertex)
    {
        while (parents[vertex] != vertex)
        {
            parents[vertex] = parents[parents[vertex]];
            vertex = parents[vertex];
        }
        return vertex;
    }
}
