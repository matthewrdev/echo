using Echo.Geometry;
using Echo.Meshing.Geometry;

namespace Echo.Meshing.Visibility;

internal static class SurfaceVisibilityEvidence
{
    public static bool Contradicts(SurfaceTriangle triangle, IReadOnlyList<DepthView> views, double relativeTolerance)
    {
        // One sample near a depth edge must not punch a triangle-sized hole.
        // Require coherent empty-space evidence across the face footprint.
        return Contradicts((triangle.A + triangle.B + triangle.C) / 3, views, relativeTolerance)
            && Contradicts((triangle.A + triangle.B) * .5, views, relativeTolerance)
            && Contradicts((triangle.B + triangle.C) * .5, views, relativeTolerance)
            && Contradicts((triangle.C + triangle.A) * .5, views, relativeTolerance);
    }

    private static bool Contradicts(V3 point, IReadOnlyList<DepthView> views, double relativeTolerance)
    {
        var supporting = 0.0;
        var contradicted = 0.0;
        var freeSpace = 0;
        foreach (var view in views)
        {
            var observation = view.Observe(point, relativeTolerance);
            if (!observation.IsValid)
            {
                continue;
            }

            var tolerance = Math.Max(observation.CameraDepth * relativeTolerance * 2,
                observation.CameraDepth / view.Focal * 4);
            // Precision weighting prevents distant flight passes from
            // outvoting a closer, more precise surface measurement.
            var precision = 1 / (tolerance * tolerance);
            if (Math.Abs(observation.Distance) <= tolerance)
            {
                supporting += precision;
            }
            else if (observation.HasFullCoverage && observation.Distance > tolerance)
            {
                freeSpace++;
                contradicted += precision;
            }
        }

        // Missing and occluded depth remain neutral.
        return freeSpace >= 3 && contradicted > supporting;
    }
}
