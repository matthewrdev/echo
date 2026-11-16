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
        IList<int> parents = scratch?.List(new IntRecordCodec()) ?? new List<int>(source.Vertices.Count);
        for (var index = 0; index < source.Vertices.Count; index++)
        {
            parents.Add(index);
        }
        foreach (var face in source.Faces)
        {
            cancellation.ThrowIfCancellationRequested();
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
        foreach (var face in source.Faces)
        {
            cancellation.ThrowIfCancellationRequested();
            var root = Root(parents, face[0]);
            var origin = new V3(source.Vertices[face[0]]);
            areas[root] += (new V3(source.Vertices[face[1]]) - origin).Cross(new V3(source.Vertices[face[2]]) - origin).Length * .5;
            counts[root]++;
            if (areas[root] > areas[largest])
            {
                largest = root;
            }
        }

        var result = work?.Surface() ?? new Surface();
        IList<int> remap = scratch?.Filled(source.Vertices.Count, -1, new IntRecordCodec()) ?? Enumerable.Repeat(-1, source.Vertices.Count).ToArray();
        foreach (var face in source.Faces)
        {
            cancellation.ThrowIfCancellationRequested();
            var root = Root(parents, face[0]);
            var smallFragment = root != largest && counts[root] <= 64 && areas[root] < voxelSize * voxelSize * 4;
            if (smallFragment)
            {
                continue;
            }

            var output = new int[3];
            for (var corner = 0; corner < 3; corner++)
            {
                var vertex = face[corner];
                var index = remap[vertex];
                if (index < 0)
                {
                    index = result.Vertices.Count;
                    remap[vertex] = index;
                    result.Vertices.Add(source.Vertices[vertex]);
                }
                output[corner] = index;
            }
            result.Faces.Add(output);
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
