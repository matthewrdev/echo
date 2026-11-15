using Echo.Geometry;

namespace Echo.Meshing;

internal static class SurfaceTriangleIntersection
{
    public static V3 Closest(V3 point, SurfaceTriangle triangle)
    {
        var ab = triangle.B - triangle.A;
        var ac = triangle.C - triangle.A;
        var ap = point - triangle.A;
        var d1 = ab.Dot(ap);
        var d2 = ac.Dot(ap);
        if (d1 <= 0 && d2 <= 0)
        {
            return triangle.A;
        }
        var bp = point - triangle.B;
        var d3 = ab.Dot(bp);
        var d4 = ac.Dot(bp);
        if (d3 >= 0 && d4 <= d3)
        {
            return triangle.B;
        }
        var vc = d1 * d4 - d3 * d2;
        var onAb = vc <= 0 && d1 >= 0 && d3 <= 0;
        if (onAb)
        {
            return triangle.A + ab * (d1 / (d1 - d3));
        }
        var cp = point - triangle.C;
        var d5 = ab.Dot(cp);
        var d6 = ac.Dot(cp);
        if (d6 >= 0 && d5 <= d6)
        {
            return triangle.C;
        }
        var vb = d5 * d2 - d1 * d6;
        var onAc = vb <= 0 && d2 >= 0 && d6 <= 0;
        if (onAc)
        {
            return triangle.A + ac * (d2 / (d2 - d6));
        }
        var va = d3 * d6 - d5 * d4;
        var onBc = va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0;
        if (onBc)
        {
            return triangle.B + (triangle.C - triangle.B) * ((d4 - d3) / (d4 - d3 + d5 - d6));
        }
        var inverse = 1 / (va + vb + vc);
        return triangle.A + ab * (vb * inverse) + ac * (vc * inverse);
    }

    public static bool Intersects(SurfaceTriangle first, SurfaceTriangle second)
    {
        var normal = (first.B - first.A).Cross(first.C - first.A).Unit;
        var otherNormal = (second.B - second.A).Cross(second.C - second.A).Unit;
        ReadOnlySpan<V3> a = [first.A, first.B, first.C];
        ReadOnlySpan<V3> b = [second.A, second.B, second.C];
        var coplanar = normal.Cross(otherNormal).Length < 1e-8
            && Math.Abs(normal.Dot(second.A - first.A)) < 1e-8;
        if (coplanar)
        {
            for (var shape = 0; shape < 2; shape++)
            {
                var points = shape == 0 ? a : b;
                for (var edge = 0; edge < 3; edge++)
                {
                    var axis = normal.Cross(points[(edge + 1) % 3] - points[edge]).Unit;
                    var lowA = Math.Min(axis.Dot(a[0]), Math.Min(axis.Dot(a[1]), axis.Dot(a[2])));
                    var highA = Math.Max(axis.Dot(a[0]), Math.Max(axis.Dot(a[1]), axis.Dot(a[2])));
                    var lowB = Math.Min(axis.Dot(b[0]), Math.Min(axis.Dot(b[1]), axis.Dot(b[2])));
                    var highB = Math.Max(axis.Dot(b[0]), Math.Max(axis.Dot(b[1]), axis.Dot(b[2])));
                    if (Math.Min(highA, highB) - Math.Max(lowA, lowB) <= 1e-8)
                    {
                        return false;
                    }
                }
            }
            return true;
        }
        for (var edge = 0; edge < 3; edge++)
        {
            if (Crosses(a[edge], a[(edge + 1) % 3], second) || Crosses(b[edge], b[(edge + 1) % 3], first))
            {
                return true;
            }
        }
        return false;
    }

    public static bool Crosses(V3 start, V3 end, SurfaceTriangle triangle)
    {
        var direction = end - start;
        var edgeA = triangle.B - triangle.A;
        var edgeB = triangle.C - triangle.A;
        var cross = direction.Cross(edgeB);
        var determinant = edgeA.Dot(cross);
        if (Math.Abs(determinant) < 1e-12)
        {
            return false;
        }
        var offset = start - triangle.A;
        var u = offset.Dot(cross) / determinant;
        var q = offset.Cross(edgeA);
        var v = direction.Dot(q) / determinant;
        var t = edgeB.Dot(q) / determinant;
        var inside = u >= -1e-8 && v >= -1e-8 && u + v <= 1 + 1e-8;
        return inside && t > 1e-8 && t < 1 - 1e-8;
    }
}
