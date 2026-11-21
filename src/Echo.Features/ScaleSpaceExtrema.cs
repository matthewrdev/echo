namespace Echo.Features;

public static class ScaleSpaceExtrema
{
    public static List<ScaleKeypoint> Find(float[][] differences, int width, int height, CancellationToken cancellation)
    {
        var result = new List<ScaleKeypoint>();
        var locations = new HashSet<int>();
        for (var layer = 1; layer <= 3; layer++)
        {
            for (var y = 5; y < height - 5; y++)
            {
                cancellation.ThrowIfCancellationRequested();
                for (var x = 5; x < width - 5; x++)
                {
                    var value = differences[layer][y * width + x];
                    if (Math.Abs(value) < .04 / 6 || !IsExtremum(differences, width, x, y, layer, value))
                    {
                        continue;
                    }

                    var point = Refine(differences, width, height, x, y, layer);
                    if (point is not { } refined)
                    {
                        continue;
                    }

                    var location = (refined.Layer * height + (int)Math.Round(refined.Y)) * width + (int)Math.Round(refined.X);
                    if (locations.Add(location))
                    {
                        result.Add(refined);
                    }
                }
            }
        }

        result.Sort(Compare);
        return result;
    }

    private static int Compare(ScaleKeypoint left, ScaleKeypoint right)
    {
        var response = right.Response.CompareTo(left.Response);
        if (response != 0)
        {
            return response;
        }

        var layer = left.Layer.CompareTo(right.Layer);
        if (layer != 0)
        {
            return layer;
        }

        var row = left.Y.CompareTo(right.Y);
        return row != 0 ? row : left.X.CompareTo(right.X);
    }

    private static bool IsExtremum(float[][] differences, int width, int x, int y, int layer, float value)
    {
        for (var scaleOffset = -1; scaleOffset <= 1; scaleOffset++)
        {
            var plane = differences[layer + scaleOffset];
            for (var rowOffset = -1; rowOffset <= 1; rowOffset++)
            {
                for (var columnOffset = -1; columnOffset <= 1; columnOffset++)
                {
                    var centre = scaleOffset == 0 && rowOffset == 0 && columnOffset == 0;
                    if (centre)
                    {
                        continue;
                    }

                    var neighbour = plane[(y + rowOffset) * width + x + columnOffset];
                    if (value > 0 ? neighbour >= value : neighbour <= value)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private static ScaleKeypoint? Refine(float[][] differences, int width, int height, int x, int y, int layer)
    {
        for (var iteration = 0; iteration < 5; iteration++)
        {
            var outside = layer is < 1 or > 3 || x < 5 || y < 5 || x >= width - 5 || y >= height - 5;
            if (outside)
            {
                return null;
            }

            var plane = differences[layer];
            var previous = differences[layer - 1];
            var next = differences[layer + 1];
            var pixel = y * width + x;
            var centre = plane[pixel];
            double gx = (plane[pixel + 1] - plane[pixel - 1]) * .5;
            double gy = (plane[pixel + width] - plane[pixel - width]) * .5;
            double gs = (next[pixel] - previous[pixel]) * .5;
            double xx = plane[pixel + 1] + plane[pixel - 1] - 2 * centre;
            double yy = plane[pixel + width] + plane[pixel - width] - 2 * centre;
            double ss = next[pixel] + previous[pixel] - 2 * centre;
            double xy = (plane[pixel + width + 1] - plane[pixel + width - 1] - plane[pixel - width + 1] + plane[pixel - width - 1]) * .25;
            double xs = (next[pixel + 1] - next[pixel - 1] - previous[pixel + 1] + previous[pixel - 1]) * .25;
            double ys = (next[pixel + width] - next[pixel - width] - previous[pixel + width] + previous[pixel - width]) * .25;
            var determinant = xx * (yy * ss - ys * ys) - xy * (xy * ss - xs * ys) + xs * (xy * ys - xs * yy);
            if (Math.Abs(determinant) < 1e-15)
            {
                return null;
            }

            var dx = -((yy * ss - ys * ys) * gx + (xs * ys - xy * ss) * gy + (xy * ys - xs * yy) * gs) / determinant;
            var dy = -((xs * ys - xy * ss) * gx + (xx * ss - xs * xs) * gy + (xy * xs - xx * ys) * gs) / determinant;
            var ds = -((xy * ys - xs * yy) * gx + (xy * xs - xx * ys) * gy + (xx * yy - xy * xy) * gs) / determinant;
            var converged = Math.Abs(dx) < .5 && Math.Abs(dy) < .5 && Math.Abs(ds) < .5;
            if (converged)
            {
                var contrast = Math.Abs(centre + .5 * (gx * dx + gy * dy + gs * ds));
                var spatialDeterminant = xx * yy - xy * xy;
                var unstable = contrast < .04 / 3 || spatialDeterminant <= 0 || (xx + yy) * (xx + yy) >= 12.1 * spatialDeterminant;
                if (unstable)
                {
                    return null;
                }

                return new ScaleKeypoint(x + dx, y + dy, layer, 1.6 * Math.Pow(2, (layer + ds) / 3), contrast);
            }

            var invalidOffset = !double.IsFinite(dx + dy + ds) || Math.Abs(dx) > 2 || Math.Abs(dy) > 2 || Math.Abs(ds) > 2;
            if (invalidOffset)
            {
                return null;
            }

            x += (int)Math.Round(dx);
            y += (int)Math.Round(dy);
            layer += (int)Math.Round(ds);
        }

        return null;
    }
}
