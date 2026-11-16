using Echo.Contracts;

namespace Echo.Geometry;

public static class GeographicCoordinates
{
    public static GeoPosition FromLocal(V3 point, GeoPosition origin)
    {
        var matrix = CameraGeometry.LocalToEcefMatrix(origin);
        var x = matrix[0] * point.X + matrix[1] * point.Y + matrix[2] * point.Z + matrix[3];
        var y = matrix[4] * point.X + matrix[5] * point.Y + matrix[6] * point.Z + matrix[7];
        var z = matrix[8] * point.X + matrix[9] * point.Y + matrix[10] * point.Z + matrix[11];
        const double radius = 6378137;
        const double eccentricity = 6.69437999014e-3;
        var horizontal = Math.Sqrt(x * x + y * y);
        var latitude = Math.Atan2(z, horizontal * (1 - eccentricity));
        for (var iteration = 0; iteration < 10; iteration++)
        {
            var sine = Math.Sin(latitude);
            var normal = radius / Math.Sqrt(1 - eccentricity * sine * sine);
            latitude = Math.Atan2(z + eccentricity * normal * sine, horizontal);
        }

        var n = radius / Math.Sqrt(1 - eccentricity * Math.Pow(Math.Sin(latitude), 2));
        return new GeoPosition
        {
            Latitude = latitude * 180 / Math.PI,
            Longitude = Math.Atan2(y, x) * 180 / Math.PI,
            Altitude = origin.Altitude.HasValue ? horizontal / Math.Cos(latitude) - n : null,
            AltitudeDatum = origin.AltitudeDatum,
            UncertaintyMetres = origin.UncertaintyMetres
        };
    }

    public static bool HasEllipsoidalHeight(GeoPosition origin)
    {
        var datum = origin.AltitudeDatum.Trim().ToLowerInvariant();
        return origin.Altitude.HasValue && datum is "wgs84 ellipsoid" or "wgs84 ellipsoidal" or "epsg:4979";
    }
}
