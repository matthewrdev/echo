using Echo.Contracts;

namespace Echo.Geometry;

public static class ExclusionGeometry
{
    public static bool Contains(IReadOnlyList<ExclusionRegion> regions, V3 point)
    {
        for (var index = 0; index < regions.Count; index++)
        {
            var region = regions[index];
            if (region.Enabled && Contains(region, point))
            {
                return true;
            }
        }
        return false;
    }

    public static bool Contains(ExclusionRegion region, V3 point)
    {
        var local = point - new V3(region.CentreX, region.CentreY, region.CentreZ);
        if (Math.Abs(local.Y) > region.Height / 2)
        {
            return false;
        }
        if (region.Shape == ExclusionShape.Box)
        {
            return Math.Abs(local.X) <= region.Width / 2 && Math.Abs(local.Z) <= region.Depth / 2;
        }
        var x = local.X / region.Radius;
        var z = local.Z / region.Radius;
        return x * x + z * z <= 1;
    }

    public static bool Intersects(IReadOnlyList<ExclusionRegion> regions, V3 a, V3 b, V3 c)
    {
        for (var index = 0; index < regions.Count; index++)
        {
            var region = regions[index];
            if (region.Enabled && Intersects(region, a, b, c))
            {
                return true;
            }
        }
        return false;
    }

    // Clip to the height slab, then the box sides or the cylinder's circular
    // cross-section. Vertex-only checks miss triangles spanning a cutout.
    public static bool Intersects(ExclusionRegion region, V3 a, V3 b, V3 c)
    {
        var centre = new V3(region.CentreX, region.CentreY, region.CentreZ);
        Span<V3> polygon = stackalloc V3[12];
        Span<V3> scratch = stackalloc V3[12];
        polygon[0] = a - centre;
        polygon[1] = b - centre;
        polygon[2] = c - centre;
        var count = Clip(polygon[..3], scratch, new V3(0, 1, 0), region.Height / 2);
        count = Clip(scratch[..count], polygon, new V3(0, -1, 0), region.Height / 2);
        if (region.Shape == ExclusionShape.Box)
        {
            count = Clip(polygon[..count], scratch, new V3(1, 0, 0), region.Width / 2);
            count = Clip(scratch[..count], polygon, new V3(-1, 0, 0), region.Width / 2);
            count = Clip(polygon[..count], scratch, new V3(0, 0, 1), region.Depth / 2);
            count = Clip(scratch[..count], polygon, new V3(0, 0, -1), region.Depth / 2);
            return count > 0;
        }
        return IntersectsCircle(polygon[..count], region.Radius);
    }

    private static int Clip(ReadOnlySpan<V3> input, Span<V3> output, V3 normal, double extent)
    {
        if (input.Length == 0)
        {
            return 0;
        }
        var count = 0;
        var previous = input[^1];
        var previousDistance = previous.Dot(normal) - extent;
        foreach (var point in input)
        {
            var distance = point.Dot(normal) - extent;
            if ((distance <= 0) != (previousDistance <= 0))
            {
                output[count++] = previous + (point - previous) * (previousDistance / (previousDistance - distance));
            }
            if (distance <= 0)
            {
                output[count++] = point;
            }
            previous = point;
            previousDistance = distance;
        }
        return count;
    }

    private static bool IntersectsCircle(ReadOnlySpan<V3> polygon, double radius)
    {
        var containsOrigin = false;
        for (var index = 0; index < polygon.Length; index++)
        {
            var a = polygon[index] / radius;
            var b = polygon[(index + 1) % polygon.Length] / radius;
            var dx = b.X - a.X;
            var dz = b.Z - a.Z;
            var lengthSquared = dx * dx + dz * dz;
            var t = lengthSquared > 0 ? Math.Clamp(-(a.X * dx + a.Z * dz) / lengthSquared, 0, 1) : 0;
            var x = a.X + t * dx;
            var z = a.Z + t * dz;
            if (x * x + z * z <= 1)
            {
                return true;
            }
            if ((a.Z > 0) != (b.Z > 0) && a.X - a.Z * dx / dz > 0)
            {
                containsOrigin = !containsOrigin;
            }
        }
        return containsOrigin;
    }
}
