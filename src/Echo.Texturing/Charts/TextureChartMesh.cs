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
        Boundary = FindBoundary();
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
    public double Area
    {
        get;
    }

    private int[] FindBoundary()
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

        // A single consistently oriented boundary and Euler characteristic one
        // are required by the convex-boundary harmonic fallback.
        if (Points.Length - edges.Count + Triangles.Count != 1)
        {
            return [];
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

        var boundary = new List<int>();
        var first = next.Keys.Min();
        var current = first;
        do
        {
            boundary.Add(current);
            if (!next.Remove(current, out current))
            {
                return [];
            }
        }
        while (current != first);
        return next.Count == 0 ? boundary.ToArray() : [];
    }
}
