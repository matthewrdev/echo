namespace Echo.Texturing.Charts;

internal static class TextureChartValidation
{
    public static bool Accept(TextureChartMesh chart, double[] coordinates, CancellationToken cancellation, bool compactAtlas = false)
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
        var severelyDistortedArea = 0.0;
        var severelyDistortedUvArea = 0.0;
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
            if (!double.IsFinite(maximum) || !(minimum > 0))
            {
                return false;
            }

            var severeStretch = minimum < .1 || maximum > 10 || maximum > minimum * 16;
            if (severeStretch)
            {
                severelyDistortedArea += doubledArea * .5;
                severelyDistortedUvArea += determinant * doubledArea * .5;
            }

            var appreciableStretch = minimum < .5 || maximum > 2 || maximum > minimum * 3;
            if (appreciableStretch)
            {
                distortedArea += doubledArea * .5;
            }
        }

        // Tiny charts consume a larger share of a compact atlas in gutters.
        // Bound severe stretch in both surface and UV area to retain density.
        var severeLimit = compactAtlas ? .02 : .005;
        var appreciableLimit = compactAtlas ? .20 : .05;
        var excessiveSevereStretch = Math.Max(severelyDistortedArea, severelyDistortedUvArea) > chart.Area * severeLimit;
        if (excessiveSevereStretch || distortedArea > chart.Area * appreciableLimit)
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
        // Positive triangle Jacobians and an oriented, simple boundary give
        // the map degree one inside the chart and zero in each hole.
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
        var edges = new List<TextureEdge>(chart.Boundary.Length);
        int[]? outer = null;
        foreach (var loop in chart.BoundaryLoops)
        {
            var area = 0.0;
            for (var index = 0; index < loop.Length; index++)
            {
                var a = loop[index];
                var b = loop[(index + 1) % loop.Length];
                edges.Add(new TextureEdge(a, b));
                area += Cross(coordinates, loop[0], a, b);
            }

            if (Math.Abs(area) <= tolerance)
            {
                return false;
            }

            if (area > 0)
            {
                if (outer != null)
                {
                    return false;
                }

                outer = loop;
            }
        }

        if (outer == null)
        {
            return false;
        }

        var holes = chart.BoundaryLoops.Where(loop => loop != outer).ToArray();
        foreach (var hole in holes)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!Contains(coordinates, outer, hole[0]))
            {
                return false;
            }

            foreach (var other in holes)
            {
                if (hole != other && Contains(coordinates, other, hole[0]))
                {
                    return false;
                }
            }
        }

        for (var first = 0; first < edges.Count; first++)
        {
            cancellation.ThrowIfCancellationRequested();
            var a = edges[first];
            for (var second = first + 1; second < edges.Count; second++)
            {
                var b = edges[second];
                var adjacent = a.A == b.B || a.B == b.A;
                if (!adjacent && Intersects(coordinates, a.A, a.B, b.A, b.B, tolerance))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool Contains(double[] uv, int[] loop, int vertex)
    {
        var x = uv[vertex * 2];
        var y = uv[vertex * 2 + 1];
        var inside = false;
        for (var index = 0; index < loop.Length; index++)
        {
            var a = loop[index] * 2;
            var b = loop[(index + 1) % loop.Length] * 2;
            var ay = uv[a + 1];
            var by = uv[b + 1];
            if ((ay > y) != (by > y) && x < uv[a] + (y - ay) * (uv[b] - uv[a]) / (by - ay))
            {
                inside = !inside;
            }
        }

        return inside;
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
