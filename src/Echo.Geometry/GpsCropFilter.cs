using Echo.Models;

namespace Echo.Geometry;

public sealed class GpsCropFilter
{
    private readonly PreparedGpsPolygon[] includes;
    private readonly PreparedGpsPolygon[] excludes;

    public GpsCropFilter(IReadOnlyList<GpsPolygon> polygons, GeoPosition? origin)
    {
        if (origin == null && polygons.Count > 0)
        {
            throw new EchoException("GPS polygons require georeferenced alignment. Align with GPS-tagged photos or camera position hints, or remove the GPS polygons.", 5);
        }

        var prepared = polygons.Select(polygon => new PreparedGpsPolygon(polygon, origin!)).ToArray();
        includes = prepared.Where(polygon => polygon.Mode == CropRegionMode.Include).ToArray();
        excludes = prepared.Where(polygon => polygon.Mode == CropRegionMode.Exclude).ToArray();
    }

    public bool HasRegions => includes.Length > 0 || excludes.Length > 0;

    public bool IsExcluded(V3 point)
    {
        var projected = new PolygonPoint(point.X, point.Z);
        foreach (var polygon in excludes)
        {
            if (polygon.Contains(projected))
            {
                return true;
            }
        }

        foreach (var polygon in includes)
        {
            if (polygon.Contains(projected))
            {
                return false;
            }
        }

        return includes.Length > 0;
    }

    public bool ExcludesTriangle(V3 a, V3 b, V3 c)
    {
        var first = new PolygonPoint(a.X, a.Z);
        var second = new PolygonPoint(b.X, b.Z);
        var third = new PolygonPoint(c.X, c.Z);
        foreach (var polygon in excludes)
        {
            if (polygon.IntersectsTriangle(first, second, third))
            {
                return true;
            }
        }

        foreach (var polygon in includes)
        {
            if (polygon.ContainsTriangle(first, second, third))
            {
                return false;
            }
        }

        return includes.Length > 0;
    }
}
