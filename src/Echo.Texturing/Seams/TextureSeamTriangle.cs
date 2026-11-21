using Echo.Geometry;

namespace Echo.Texturing.Seams;

internal readonly record struct TextureSeamTriangle(V3 A, V3 B, V3 C)
{
    public bool Contains(V3 point)
    {
        var first = Cross(B - A, point - A);
        var second = Cross(C - B, point - B);
        var third = Cross(A - C, point - C);
        var positive = first >= -1e-7 && second >= -1e-7 && third >= -1e-7;
        var negative = first <= 1e-7 && second <= 1e-7 && third <= 1e-7;
        return positive || negative;
    }

    public V3 InsideEdge(double fraction)
    {
        var edge = B - A;
        var normal = new V3(-edge.Y, edge.X, 0).Unit;
        var height = (C - A).Dot(normal);
        var offset = Math.CopySign(Math.Min(.75, Math.Abs(height) * .25), height);
        return A + edge * fraction + normal * offset;
    }

    private double Cross(V3 a, V3 b) => a.X * b.Y - a.Y * b.X;
}
