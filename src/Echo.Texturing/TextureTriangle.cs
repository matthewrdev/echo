using SkiaSharp;

namespace Echo.Texturing;

internal sealed class TextureTriangle
{
    public SKPoint[] Points { get; set; } = [];
    public SKRect Bounds
    {
        get; set;
    }

    public bool Overlaps(TextureTriangle other)
    {
        if (!Bounds.IntersectsWith(other.Bounds))
        {
            return false;
        }

        for (var a = 0; a < 3; a++)
        {
            for (var b = 0; b < 3; b++)
            {
                if (Crosses(Points[a], Points[(a + 1) % 3], other.Points[b], other.Points[(b + 1) % 3]))
                {
                    return true;
                }
            }
        }

        return Points.Any(other.Contains) || other.Points.Any(Contains)
            || Contains(new SKPoint(other.Points.Average(p => p.X), other.Points.Average(p => p.Y)));
    }

    private bool Contains(SKPoint point)
    {
        var a = Cross(Points[0], Points[1], point);
        var b = Cross(Points[1], Points[2], point);
        var c = Cross(Points[2], Points[0], point);
        var positive = a > 1e-8 && b > 1e-8 && c > 1e-8;
        var negative = a < -1e-8 && b < -1e-8 && c < -1e-8;
        return positive || negative;
    }

    private bool Crosses(SKPoint a, SKPoint b, SKPoint c, SKPoint d)
    {
        return Cross(a, b, c) * Cross(a, b, d) < -1e-12 && Cross(c, d, a) * Cross(c, d, b) < -1e-12;
    }

    private double Cross(SKPoint a, SKPoint b, SKPoint c)
    {
        return (double)(b.X - a.X) * (c.Y - a.Y) - (double)(b.Y - a.Y) * (c.X - a.X);
    }
}
