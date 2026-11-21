using Echo.Models;

namespace Echo.Geometry;

public static class CropHullGeometry
{
    public static bool Contains(CropHull hull, V3 point)
    {
        foreach (var plane in hull.Planes)
        {
            if (Distance(plane, point) > hull.Tolerance)
            {
                return false;
            }
        }

        return true;
    }

    public static bool Intersects(CropHull hull, V3 a, V3 b, V3 c)
    {
        // Clipping a convex polygon by one plane adds at most one vertex.
        Span<V3> polygon = stackalloc V3[hull.Planes.Length + 3];
        Span<V3> scratch = stackalloc V3[hull.Planes.Length + 3];
        polygon[0] = a;
        polygon[1] = b;
        polygon[2] = c;
        var count = 3;
        foreach (var plane in hull.Planes)
        {
            var written = 0;
            var previous = polygon[count - 1];
            var previousDistance = Distance(plane, previous) - hull.Tolerance;
            for (var index = 0; index < count; index++)
            {
                var point = polygon[index];
                var distance = Distance(plane, point) - hull.Tolerance;
                if ((distance <= 0) != (previousDistance <= 0))
                {
                    scratch[written++] = previous + (point - previous) * (previousDistance / (previousDistance - distance));
                }

                if (distance <= 0)
                {
                    scratch[written++] = point;
                }

                previous = point;
                previousDistance = distance;
            }

            if (written == 0)
            {
                return false;
            }

            scratch[..written].CopyTo(polygon);
            count = written;
        }

        return true;
    }

    private static double Distance(CropPlane plane, V3 point) =>
        point.X * plane.NormalX + point.Y * plane.NormalY + point.Z * plane.NormalZ - plane.Offset;
}
