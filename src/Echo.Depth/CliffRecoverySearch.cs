using Echo.Models;
using Echo.Geometry;

namespace Echo.Depth;

public static class CliffRecoverySearch
{
    public static double Anchor(DepthMap source, int x, int y)
    {
        Span<float> depths = stackalloc float[80];
        var count = 0;
        var quadrants = 0;
        for (var dy = -4; dy <= 4; dy++)
        {
            for (var dx = -4; dx <= 4; dx++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                var depth = source.DepthAt((y + dy) * source.Width + x + dx);
                if (float.IsFinite(depth) && depth > 0)
                {
                    depths[count++] = depth;
                    if (dx != 0 && dy != 0)
                    {
                        quadrants |= 1 << ((dx > 0 ? 1 : 0) + (dy > 0 ? 2 : 0));
                    }
                }
            }
        }
        // Bounded interior gaps only. Do not extrapolate past a silhouette or
        // seed a new surface from a single side of a depth discontinuity.
        if (count < 8 || quadrants != 15)
        {
            return 0;
        }

        depths[..count].Sort();
        var median = depths[count / 2];
        return depths[count - 1] - depths[0] <= median * .04 ? median : 0;
    }

    public static bool Supported(DepthView reference, IReadOnlyList<DepthView> neighbours, V3 point, int x, int y)
    {
        var support = 0;
        foreach (var view in neighbours)
        {
            var cosine = Math.Clamp((point - reference.Centre).Unit.Dot((point - view.Centre).Unit), -1, 1);
            var usefulParallax = Math.Acos(cosine) >= .5 * Math.PI / 180;
            var agrees = usefulParallax && view.TrySurfacePoint(point, .005, out var surface)
                && reference.ReprojectsTo(surface, x, y, 1);
            if (agrees && ++support >= 2)
            {
                return true;
            }
        }

        return false;
    }
}
