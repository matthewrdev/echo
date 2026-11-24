namespace Echo.Models;

public static class PolygonGeometry
{
    public static double Cross(PolygonPoint a, PolygonPoint b, PolygonPoint c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    public static bool OnSegment(PolygonPoint a, PolygonPoint b, PolygonPoint point) =>
        Cross(a, b, point) == 0
        && point.X >= Math.Min(a.X, b.X) && point.X <= Math.Max(a.X, b.X)
        && point.Y >= Math.Min(a.Y, b.Y) && point.Y <= Math.Max(a.Y, b.Y);

    public static bool Intersects(PolygonPoint a, PolygonPoint b, PolygonPoint c, PolygonPoint d)
    {
        var cSide = Math.Sign(Cross(a, b, c));
        var dSide = Math.Sign(Cross(a, b, d));
        var aSide = Math.Sign(Cross(c, d, a));
        var bSide = Math.Sign(Cross(c, d, b));
        return cSide * dSide < 0 && aSide * bSide < 0
            || cSide == 0 && OnSegment(a, b, c)
            || dSide == 0 && OnSegment(a, b, d)
            || aSide == 0 && OnSegment(c, d, a)
            || bSide == 0 && OnSegment(c, d, b);
    }
}
