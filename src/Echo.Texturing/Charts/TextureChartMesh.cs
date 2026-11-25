using System.Runtime.InteropServices;
using Echo.Geometry;

namespace Echo.Texturing.Charts;

internal sealed class TextureChartMesh
{
    public TextureChartMesh(TextureChartGeometry geometry, IReadOnlyList<int> faces)
    {
        var points = new List<V3>();
        foreach (var face in faces)
        {
            var vertices = geometry.Surface.Faces[face];
            var triangle = new int[3];
            for (var corner = 0; corner < 3; corner++)
            {
                var vertex = vertices[corner];
                var welded = geometry.WeldedVertices[vertex];
                if (!LocalVertices.TryGetValue(welded, out var local))
                {
                    local = points.Count;
                    LocalVertices.Add(welded, local);
                    points.Add(geometry.Points[vertex]);
                }

                triangle[corner] = local;
            }

            Triangles.Add(triangle);
            Area += geometry.Areas[face];
        }

        Points = points.ToArray();
        BoundaryLoops = FindBoundaryLoops();
        Boundary = BoundaryLoops.SelectMany(loop => loop).ToArray();
    }

    public Dictionary<int, int> LocalVertices { get; } = [];
    public V3[] Points
    {
        get;
    }
    public List<int[]> Triangles { get; } = [];
    public int[] Boundary
    {
        get;
    }
    public int[][] BoundaryLoops
    {
        get;
    }
    public double Area
    {
        get;
    }

    private int[][] FindBoundaryLoops()
    {
        var edges = new Dictionary<TextureEdge, int>();
        foreach (var triangle in Triangles)
        {
            for (var corner = 0; corner < 3; corner++)
            {
                var a = triangle[corner];
                var b = triangle[(corner + 1) % 3];
                var edge = new TextureEdge(Math.Min(a, b), Math.Max(a, b));
                // Single edges retain their direction. A pair must use opposite
                // directions, and no edge may occur a third time.
                var direction = a == edge.A ? 1 : -1;
                ref var state = ref CollectionsMarshal.GetValueRefOrAddDefault(edges, edge, out var exists);
                if (exists && state != -direction)
                {
                    return [];
                }

                state = exists ? 2 : direction;
            }
        }

        var next = new Dictionary<int, int>();
        foreach (var edge in edges)
        {
            if (edge.Value == 2)
            {
                continue;
            }

            var a = edge.Value == 1 ? edge.Key.A : edge.Key.B;
            var b = edge.Value == 1 ? edge.Key.B : edge.Key.A;
            if (!next.TryAdd(a, b))
            {
                return [];
            }
        }

        if (next.Count < 3)
        {
            return [];
        }

        var loops = new List<int[]>();
        foreach (var start in next.Keys.Order().ToArray())
        {
            if (!next.ContainsKey(start))
            {
                continue;
            }

            var loop = new List<int>();
            var current = start;
            do
            {
                loop.Add(current);
                if (!next.Remove(current, out current))
                {
                    return [];
                }
            }
            while (current != start);

            if (loop.Count < 3)
            {
                return [];
            }

            loops.Add(loop.ToArray());
        }

        // A planar chart may have holes. Euler characteristic rules out
        // handles even when all boundary edges form individually valid loops.
        var euler = Points.Length - edges.Count + Triangles.Count;
        return euler == 2 - loops.Count ? loops.ToArray() : [];
    }
}
