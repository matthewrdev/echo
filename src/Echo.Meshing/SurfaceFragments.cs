using Echo.Contracts;
using Echo.Geometry;

namespace Echo.Meshing;

internal static class SurfaceFragments
{
    public static Surface RemoveSmall(Surface source, double voxelSize, CancellationToken cancellation)
    {
        var parents = Enumerable.Range(0, source.Vertices.Count).ToArray();
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

        var counts = new int[parents.Length];
        var areas = new double[parents.Length];
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

        var result = new Surface();
        var remap = new Dictionary<int, int>();
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
                if (!remap.TryGetValue(vertex, out var index))
                {
                    index = result.Vertices.Count;
                    remap.Add(vertex, index);
                    result.Vertices.Add(source.Vertices[vertex]);
                }
                output[corner] = index;
            }
            result.Faces.Add(output);
        }
        return result;
    }

    private static int Root(int[] parents, int vertex)
    {
        while (parents[vertex] != vertex)
        {
            parents[vertex] = parents[parents[vertex]];
            vertex = parents[vertex];
        }
        return vertex;
    }
}
