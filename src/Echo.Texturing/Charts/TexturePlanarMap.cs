using Echo.Geometry;

namespace Echo.Texturing.Charts;

internal static class TexturePlanarMap
{
    public static double[]? Solve(TextureChartMesh chart, CancellationToken cancellation)
    {
        if (chart.Boundary.Length < 3)
        {
            return null;
        }

        var normal = default(V3);
        foreach (var triangle in chart.Triangles)
        {
            cancellation.ThrowIfCancellationRequested();
            var a = chart.Points[triangle[0]];
            normal += (chart.Points[triangle[1]] - a).Cross(chart.Points[triangle[2]] - a);
        }

        if (!(normal.Length > 0))
        {
            return null;
        }

        normal = normal.Unit;
        var helper = Math.Abs(normal.Y) < .9 ? new V3(0, 1, 0) : new V3(1, 0, 0);
        var u = helper.Cross(normal).Unit;
        var v = normal.Cross(u);
        var origin = chart.Points[0];
        var coordinates = new double[chart.Points.Length * 2];
        for (var index = 0; index < chart.Points.Length; index++)
        {
            var position = chart.Points[index] - origin;
            coordinates[index * 2] = position.Dot(u);
            coordinates[index * 2 + 1] = position.Dot(v);
        }

        return coordinates;
    }
}
