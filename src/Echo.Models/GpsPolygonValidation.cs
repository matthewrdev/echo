namespace Echo.Models;

public static class GpsPolygonValidation
{
    public static void ValidateAll(IReadOnlyList<GpsPolygon> polygons)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var polygon in polygons)
        {
            if (polygon == null || !ids.Add(polygon.Id))
            {
                throw new EchoException("GPS polygons must be objects with unique IDs.", 2);
            }

            Validate(polygon);
        }
    }

    private static void Validate(GpsPolygon polygon)
    {
        if (string.IsNullOrWhiteSpace(polygon.Id) || !Enum.IsDefined(polygon.Mode))
        {
            throw new EchoException("GPS polygons need an ID and an include or exclude mode.", 2);
        }

        var vertices = polygon.Vertices;
        if (vertices == null || vertices.Count < 3 || vertices.Any(InvalidLocation))
        {
            throw new EchoException("GPS polygons need at least three valid latitude and longitude vertices within ±85° latitude.", 2);
        }

        var origin = vertices[0];
        var ring = vertices.Select(vertex => new PolygonPoint(
            Math.IEEERemainder(vertex.Longitude - origin.Longitude, 360),
            vertex.Latitude - origin.Latitude)).ToArray();

        if (ring.Distinct().Count() != ring.Length)
        {
            throw new EchoException("GPS polygon vertices must be distinct. The closing edge is added automatically.", 2);
        }

        if (ring.Max(point => point.X) - ring.Min(point => point.X) >= 180)
        {
            throw new EchoException("A GPS polygon must span less than 180° longitude.", 2);
        }

        var area = 0.0;
        for (var index = 0; index < ring.Length; index++)
        {
            var next = (index + 1) % ring.Length;
            var previous = (index + ring.Length - 1) % ring.Length;
            area += PolygonGeometry.Cross(default, ring[index], ring[next]);
            if (PolygonGeometry.OnSegment(ring[previous], ring[index], ring[next])
                || PolygonGeometry.OnSegment(ring[index], ring[next], ring[previous]))
            {
                throw new EchoException("GPS polygon edges cannot overlap.", 2);
            }

            for (var other = index + 2; other < ring.Length; other++)
            {
                var otherNext = (other + 1) % ring.Length;
                if (otherNext != index && PolygonGeometry.Intersects(ring[index], ring[next], ring[other], ring[otherNext]))
                {
                    throw new EchoException("GPS polygon edges cannot cross or touch each other.", 2);
                }
            }
        }

        if (area == 0)
        {
            throw new EchoException("A GPS polygon must enclose an area.", 2);
        }
    }

    private static bool InvalidLocation(GpsPolygonVertex? vertex)
    {
        return vertex == null || !double.IsFinite(vertex.Latitude) || !double.IsFinite(vertex.Longitude)
            || Math.Abs(vertex.Latitude) > 85 || Math.Abs(vertex.Longitude) > 180;
    }
}
