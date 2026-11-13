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

        for (var corner = 0; corner < 3; corner++)
        {
            if (other.Contains(Points[corner]) || Contains(other.Points[corner]))
            {
                return true;
            }
        }
        var centreX = (float)(((double)other.Points[0].X + other.Points[1].X + other.Points[2].X) / 3);
        var centreY = (float)(((double)other.Points[0].Y + other.Points[1].Y + other.Points[2].Y) / 3);
        return Contains(new SKPoint(centreX, centreY));
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

    private bool Crosses(SKPoint firstStart, SKPoint firstEnd, SKPoint secondStart, SKPoint secondEnd)
    {
        return Cross(firstStart, firstEnd, secondStart) * Cross(firstStart, firstEnd, secondEnd) < -1e-12 && Cross(secondStart, secondEnd, firstStart) * Cross(secondStart, secondEnd, firstEnd) < -1e-12;
    }

    private double Cross(SKPoint a, SKPoint b, SKPoint c)
    {
        return (double)(b.X - a.X) * (c.Y - a.Y) - (double)(b.Y - a.Y) * (c.X - a.X);
    }
}
