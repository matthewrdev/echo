using Echo.Geometry;

namespace Echo.Meshing.Repair;

internal static class GroundProjection
{
    public static bool Overlaps(ReadOnlySpan<V3> first, ReadOnlySpan<V3> second)
    {
        for (var shape = 0; shape < 2; shape++)
        {
            var triangle = shape == 0 ? first : second;
            for (var edge = 0; edge < 3; edge++)
            {
                var direction = triangle[(edge + 1) % 3] - triangle[edge];
                var axis = new V3(-direction.Z, 0, direction.X);
                var minimumA = double.PositiveInfinity;
                var maximumA = double.NegativeInfinity;
                var minimumB = double.PositiveInfinity;
                var maximumB = double.NegativeInfinity;
                for (var corner = 0; corner < 3; corner++)
                {
                    var a = first[corner].Dot(axis);
                    var b = second[corner].Dot(axis);
                    minimumA = Math.Min(minimumA, a);
                    maximumA = Math.Max(maximumA, a);
                    minimumB = Math.Min(minimumB, b);
                    maximumB = Math.Max(maximumB, b);
                }
                if (Math.Min(maximumA, maximumB) - Math.Max(minimumA, minimumB) <= axis.Length * 1e-9)
                {
                    return false;
                }
            }
        }
        return true;
    }

    public static bool Contains(ReadOnlySpan<V3> triangle, V3 point)
    {
        // Ground triangles are wound upwards, hence clockwise in X/Z.
        for (var corner = 0; corner < 3; corner++)
        {
            var edge = triangle[(corner + 1) % 3] - triangle[corner];
            if (edge.Cross(point - triangle[corner]).Y < -1e-9 * edge.Length)
            {
                return false;
            }
        }
        return true;
    }
}
