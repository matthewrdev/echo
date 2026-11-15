using Echo.Geometry;

namespace Echo.Texturing;

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
        var counts = new Dictionary<TextureEdge, int>();
        foreach (var triangle in Triangles)
        {
            for (var corner = 0; corner < 3; corner++)
            {
                var a = triangle[corner];
                var b = triangle[(corner + 1) % 3];
                var edge = new TextureEdge(Math.Min(a, b), Math.Max(a, b));
                if (edges.TryGetValue(edge, out var start) && start == a)
                {
                    return [];
                }
                counts[edge] = counts.GetValueOrDefault(edge) + 1;
                edges[edge] = a;
            }
        }

        // A single consistently oriented boundary and Euler characteristic one
        // are required by the convex-boundary harmonic fallback.
        if (Points.Length - edges.Count + Triangles.Count != 1 || counts.Values.Any(count => count > 2))
        {
            return [];
        }
        var next = new Dictionary<int, int>();
        foreach (var edge in counts.Where(entry => entry.Value == 1).Select(entry => entry.Key))
        {
            var a = edges[edge];
            var b = a == edge.A ? edge.B : edge.A;
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
