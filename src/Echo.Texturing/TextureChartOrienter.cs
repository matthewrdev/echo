using SkiaSharp;

namespace Echo.Texturing;

public sealed class TextureChartOrienter : ITextureChartOrienter
{
    public TextureChartOrienter()
    {
    }

    public void Orient(TexturePatch patch)
    {
        var unique = new HashSet<SKPoint>();
        foreach (var point in patch.Coordinates.Values)
        {
            unique.Add(new SKPoint((float)point[0], (float)point[1]));
        }
        var points = new SKPoint[unique.Count];
        unique.CopyTo(points);
        Array.Sort(points, (a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
        var hull = Hull(points);
        var bestArea = double.PositiveInfinity;
        var cosine = 1.0;
        var sine = 0.0;
        for (var edge = 0; edge < hull.Count; edge++)
        {
            var start = hull[edge];
            var end = hull[(edge + 1) % hull.Count];
            var angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
            var c = Math.Cos(angle);
            var s = Math.Sin(angle);
            var left = double.PositiveInfinity;
            var top = double.PositiveInfinity;
            var right = double.NegativeInfinity;
            var bottom = double.NegativeInfinity;
            foreach (var point in hull)
            {
                var x = point.X * c + point.Y * s;
                var y = -point.X * s + point.Y * c;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
            var width = right - left;
            var height = bottom - top;
            if (width * height < bestArea)
            {
                bestArea = width * height;
                cosine = c;
                sine = s;
            }
        }

        var minimumX = double.PositiveInfinity;
        var minimumY = double.PositiveInfinity;
        var maximumX = double.NegativeInfinity;
        var maximumY = double.NegativeInfinity;
        foreach (var point in patch.Coordinates.Values)
        {
            var x = point[0] * cosine + point[1] * sine;
            var y = -point[0] * sine + point[1] * cosine;
            point[0] = x;
            point[1] = y;
            minimumX = Math.Min(minimumX, x);
            minimumY = Math.Min(minimumY, y);
            maximumX = Math.Max(maximumX, x);
            maximumY = Math.Max(maximumY, y);
        }
        patch.Bounds = new SKRect((float)minimumX, (float)minimumY, (float)maximumX, (float)maximumY);
    }

    private List<SKPoint> Hull(SKPoint[] points)
    {
        var lower = new List<SKPoint>();
        var upper = new List<SKPoint>();
        foreach (var point in points)
        {
            Append(lower, point);
        }

        for (var index = points.Length - 1; index >= 0; index--)
        {
            Append(upper, points[index]);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    private void Append(List<SKPoint> hull, SKPoint point)
    {
        while (hull.Count >= 2)
        {
            var a = hull[^2];
            var b = hull[^1];
            var cross = (double)(b.X - a.X) * (point.Y - a.Y) - (double)(b.Y - a.Y) * (point.X - a.X);
            if (cross > 0)
            {
                break;
            }

            hull.RemoveAt(hull.Count - 1);
        }

        hull.Add(point);
    }
}
