using Echo.Models;

namespace Echo.Geometry;

internal sealed class PreparedGpsPolygon
{
    private readonly PolygonPoint[] ring;
    private readonly double minX;
    private readonly double maxX;
    private readonly double minY;
    private readonly double maxY;

    public PreparedGpsPolygon(GpsPolygon polygon, GeoPosition origin)
    {
        ring = polygon.Vertices.Select(vertex =>
        {
            var point = CameraGeometry.ToLocal(new GeoPosition
            {
                Latitude = vertex.Latitude,
                Longitude = vertex.Longitude,
                Altitude = origin.Altitude
            }, origin);
            return new PolygonPoint(point.X, point.Z);
        }).ToArray();
        minX = ring.Min(point => point.X);
        maxX = ring.Max(point => point.X);
        minY = ring.Min(point => point.Y);
        maxY = ring.Max(point => point.Y);
        Mode = polygon.Mode;
    }

    public CropRegionMode Mode
    {
        get;
    }

    public bool Contains(PolygonPoint point)
    {
        var outsideX = point.X < minX || point.X > maxX;
        var outsideY = point.Y < minY || point.Y > maxY;
        if (outsideX || outsideY)
        {
            return false;
        }

        var inside = false;
        var previous = ring[^1];
        foreach (var next in ring)
        {
            if (PolygonGeometry.OnSegment(previous, next, point))
            {
                return true;
            }

            var straddles = (previous.Y > point.Y) != (next.Y > point.Y);
            if (straddles && point.X < previous.X + (next.X - previous.X) * (point.Y - previous.Y) / (next.Y - previous.Y))
            {
                inside = !inside;
            }

            previous = next;
        }

        return inside;
    }

    public bool IntersectsTriangle(PolygonPoint a, PolygonPoint b, PolygonPoint c)
    {
        var outsideX = Math.Max(a.X, Math.Max(b.X, c.X)) < minX || Math.Min(a.X, Math.Min(b.X, c.X)) > maxX;
        var outsideY = Math.Max(a.Y, Math.Max(b.Y, c.Y)) < minY || Math.Min(a.Y, Math.Min(b.Y, c.Y)) > maxY;
        if (outsideX || outsideY)
        {
            return false;
        }

        return Contains(a) || Contains(b) || Contains(c) || HasBoundaryInsideTriangle(a, b, c);
    }

    // Reject triangles on the cut boundary. This also prevents triangles
    // from bridging across the notch of a concave inclusion.
    public bool ContainsTriangle(PolygonPoint a, PolygonPoint b, PolygonPoint c) =>
        Contains(a) && Contains(b) && Contains(c) && !HasBoundaryInsideTriangle(a, b, c);

    private bool HasBoundaryInsideTriangle(PolygonPoint a, PolygonPoint b, PolygonPoint c)
    {
        var orientation = Math.Sign(PolygonGeometry.Cross(a, b, c));
        var previous = ring[^1];
        foreach (var next in ring)
        {
            var intersectsEdge = PolygonGeometry.Intersects(a, b, previous, next)
                || PolygonGeometry.Intersects(b, c, previous, next)
                || PolygonGeometry.Intersects(c, a, previous, next);
            var insideTriangle = orientation != 0
                && Math.Sign(PolygonGeometry.Cross(a, b, next)) == orientation
                && Math.Sign(PolygonGeometry.Cross(b, c, next)) == orientation
                && Math.Sign(PolygonGeometry.Cross(c, a, next)) == orientation;
            if (intersectsEdge || insideTriangle)
            {
                return true;
            }

            previous = next;
        }

        return false;
    }
}
