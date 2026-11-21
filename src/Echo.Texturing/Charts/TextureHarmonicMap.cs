using Echo.Models;
using Echo.Compute;

namespace Echo.Texturing.Charts;

internal static class TextureHarmonicMap
{
    // Tutte's positive-weight barycentric embedding on a convex boundary.
    // This fallback handles folds that defeat the free-boundary conformal map.
    // It is still rejected when stretch is excessive, even if it is injective.
    public static double[]? Solve(TextureChartMesh chart, CancellationToken cancellation, IComputeService? compute = null, ComputeBackend backend = ComputeBackend.Auto)
    {
        if (chart.Boundary.Length < 3)
        {
            return null;
        }
        var points = chart.Points;
        var edges = new HashSet<TextureEdge>();
        foreach (var triangle in chart.Triangles)
        {
            for (var corner = 0; corner < 3; corner++)
            {
                var a = triangle[corner];
                var b = triangle[(corner + 1) % 3];
                edges.Add(new TextureEdge(Math.Min(a, b), Math.Max(a, b)));
            }
        }
        var system = new TextureLinearSystem(points.Length);
        foreach (var edge in edges)
        {
            system.Add(edge.A, edge.A, 1);
            system.Add(edge.B, edge.B, 1);
            system.Add(edge.A, edge.B, -1);
            system.Add(edge.B, edge.A, -1);
        }
        var lengths = new double[chart.Boundary.Length];
        for (var index = 0; index < lengths.Length; index++)
        {
            lengths[index] = (points[chart.Boundary[index]] - points[chart.Boundary[(index + 1) % lengths.Length]]).Length;
        }
        var circumference = lengths.Sum();
        if (!(circumference > 0))
        {
            return null;
        }
        var u = new Dictionary<int, double>();
        var v = new Dictionary<int, double>();
        var distance = 0.0;
        for (var index = 0; index < lengths.Length; index++)
        {
            var angle = distance / circumference * 2 * Math.PI;
            u[chart.Boundary[index]] = Math.Cos(angle);
            v[chart.Boundary[index]] = Math.Sin(angle);
            distance += lengths[index];
        }
        var x = system.Solve(u, cancellation, compute, backend);
        var y = system.Solve(v, cancellation, compute, backend);
        if (x == null || y == null)
        {
            return null;
        }
        var coordinates = new double[points.Length * 2];
        for (var vertex = 0; vertex < points.Length; vertex++)
        {
            coordinates[vertex * 2] = x[vertex];
            coordinates[vertex * 2 + 1] = y[vertex];
        }
        return coordinates;
    }
}
