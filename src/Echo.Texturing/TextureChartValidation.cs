namespace Echo.Texturing;

internal static class TextureChartValidation
{
    public static bool Accept(TextureChartMesh chart, double[] coordinates, CancellationToken cancellation)
    {
        if (coordinates.Any(value => !double.IsFinite(value)) || chart.Boundary.Length < 3)
        {
            return false;
        }
        var area = 0.0;
        foreach (var triangle in chart.Triangles)
        {
            var determinant = Cross(coordinates, triangle[0], triangle[1], triangle[2]);
            if (!(determinant > 0))
            {
                return false;
            }
            area += determinant * .5;
        }
        if (!(chart.Area > 0) || !double.IsFinite(area))
        {
            return false;
        }
        // Equal total surface and UV areas give density a consistent world-space
        // meaning. Stretch tests then bound both anisotropy and local scale.
        var scale = Math.Sqrt(chart.Area / area);
        for (var index = 0; index < coordinates.Length; index++)
        {
            coordinates[index] *= scale;
        }
        var distortedArea = 0.0;
        foreach (var triangle in chart.Triangles)
        {
            cancellation.ThrowIfCancellationRequested();
            var a = triangle[0];
            var b = triangle[1];
            var c = triangle[2];
            var edge = chart.Points[b] - chart.Points[a];
            var other = chart.Points[c] - chart.Points[a];
            var length = edge.Length;
            var doubledArea = edge.Cross(other).Length;
            if (!(length > 0) || !(doubledArea > 0))
            {
                return false;
            }
            var x = edge.Dot(other) / length;
            var y = doubledArea / length;
            var j00 = (coordinates[b * 2] - coordinates[a * 2]) / length;
            var j10 = (coordinates[b * 2 + 1] - coordinates[a * 2 + 1]) / length;
            var j01 = (coordinates[c * 2] - coordinates[a * 2] - j00 * x) / y;
            var j11 = (coordinates[c * 2 + 1] - coordinates[a * 2 + 1] - j10 * x) / y;
            var determinant = j00 * j11 - j01 * j10;
            var trace = j00 * j00 + j01 * j01 + j10 * j10 + j11 * j11;
            var maximum = Math.Sqrt((trace + Math.Sqrt(Math.Max(0, trace * trace - 4 * determinant * determinant))) * .5);
            var minimum = determinant / maximum;
            var severeStretch = minimum < .1 || maximum > 10 || maximum > minimum * 16;
            if (severeStretch || !double.IsFinite(maximum))
            {
                return false;
            }
            var appreciableStretch = minimum < .5 || maximum > 2 || maximum > minimum * 3;
            if (appreciableStretch)
            {
                distortedArea += doubledArea * .5;
            }
        }
        if (distortedArea > chart.Area * .05)
        {
            return false;
        }

        return HasSimpleBoundary(chart, coordinates, cancellation);
    }

    public static bool HasSimpleBoundary(TextureChartMesh chart, double[] coordinates, CancellationToken cancellation)
    {
        if (chart.Boundary.Length < 3)
        {
            return false;
        }
        // For an oriented triangulated disk, positive triangle Jacobians plus a
        // simple boundary imply a globally injective piecewise-linear map. Test
        // boundary crossings AND touching; positive areas alone are insufficient.
        var boundary = chart.Boundary;
        var minimumX = double.PositiveInfinity;
        var minimumY = double.PositiveInfinity;
        var maximumX = double.NegativeInfinity;
        var maximumY = double.NegativeInfinity;
        for (var vertex = 0; vertex < coordinates.Length; vertex += 2)
        {
            minimumX = Math.Min(minimumX, coordinates[vertex]);
            minimumY = Math.Min(minimumY, coordinates[vertex + 1]);
            maximumX = Math.Max(maximumX, coordinates[vertex]);
            maximumY = Math.Max(maximumY, coordinates[vertex + 1]);
        }
        var extent = Math.Max(maximumX - minimumX, maximumY - minimumY);
        var tolerance = extent * extent * 1e-14;
        for (var first = 0; first < boundary.Length; first++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (var second = first + 2; second < boundary.Length; second++)
            {
                if (first == 0 && second == boundary.Length - 1)
                {
                    continue;
                }
                if (Intersects(coordinates, boundary[first], boundary[(first + 1) % boundary.Length],
                    boundary[second], boundary[(second + 1) % boundary.Length], tolerance))
                {
                    return false;
                }
            }
        }
        return true;
    }

    private static bool Intersects(double[] uv, int a, int b, int c, int d, double tolerance)
    {
        var separatedX = Math.Max(uv[a * 2], uv[b * 2]) < Math.Min(uv[c * 2], uv[d * 2])
            || Math.Max(uv[c * 2], uv[d * 2]) < Math.Min(uv[a * 2], uv[b * 2]);
        var separatedY = Math.Max(uv[a * 2 + 1], uv[b * 2 + 1]) < Math.Min(uv[c * 2 + 1], uv[d * 2 + 1])
            || Math.Max(uv[c * 2 + 1], uv[d * 2 + 1]) < Math.Min(uv[a * 2 + 1], uv[b * 2 + 1]);
        if (separatedX || separatedY)
        {
            return false;
        }
        var first = Cross(uv, a, b, c);
        var second = Cross(uv, a, b, d);
        var third = Cross(uv, c, d, a);
        var fourth = Cross(uv, c, d, b);
        var firstStraddles = Math.Min(first, second) <= tolerance && Math.Max(first, second) >= -tolerance;
        var secondStraddles = Math.Min(third, fourth) <= tolerance && Math.Max(third, fourth) >= -tolerance;
        return firstStraddles && secondStraddles;
    }

    private static double Cross(double[] uv, int a, int b, int c)
    {
        return (uv[b * 2] - uv[a * 2]) * (uv[c * 2 + 1] - uv[a * 2 + 1])
            - (uv[b * 2 + 1] - uv[a * 2 + 1]) * (uv[c * 2] - uv[a * 2]);
    }
}
