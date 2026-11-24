using Echo.Models;

namespace Echo.Geometry;

public sealed class RegionCropFilter
{
    private readonly PreparedCropRegion[] includes;
    private readonly PreparedCropRegion[] excludes;

    private readonly GpsCropFilter? gps;

    public RegionCropFilter(IReadOnlyList<ExclusionRegion> regions, IReadOnlyList<GpsPolygon>? gpsPolygons = null, GeoPosition? origin = null)
    {
        gps = gpsPolygons is { Count: > 0 } ? new GpsCropFilter(gpsPolygons, origin) : null;
        includes = regions.Where(region => region.Enabled && region.Mode == CropRegionMode.Include)
            .Select(region => new PreparedCropRegion(region)).ToArray();
        excludes = regions.Where(region => region.Enabled && region.Mode == CropRegionMode.Exclude)
            .Select(region => new PreparedCropRegion(region)).ToArray();
    }

    public bool HasRegions => includes.Length > 0 || excludes.Length > 0 || gps != null;

    public bool IsExcluded(V3 point)
    {
        if (gps?.IsExcluded(point) == true)
        {
            return true;
        }

        foreach (var region in excludes)
        {
            if (region.Contains(point))
            {
                return true;
            }
        }

        foreach (var region in includes)
        {
            if (region.Contains(point))
            {
                return false;
            }
        }

        return includes.Length > 0;
    }

    public bool ExcludesTriangle(V3 a, V3 b, V3 c)
    {
        if (gps?.ExcludesTriangle(a, b, c) == true)
        {
            return true;
        }

        foreach (var region in excludes)
        {
            if (region.Intersects(a, b, c))
            {
                return true;
            }
        }
        // Crop volumes are convex. Requiring a whole triangle in one include
        // region prevents reconstruction from bridging the space between them.
        foreach (var region in includes)
        {
            var contained = region.Contains(a) && region.Contains(b) && region.Contains(c);
            if (contained)
            {
                return false;
            }
        }

        return includes.Length > 0;
    }
}
