using Echo.Models;

namespace Echo.Pipeline.Runs;

internal static class GpsCropFingerprint
{
    public static object Apply(object inputs, IReadOnlyList<GpsPolygon>? polygons)
    {
        return polygons is { Count: > 0 }
            ? new
            {
                inputs,
                gpsCropVersion = 1,
                gpsPolygons = polygons
            }
            : inputs;
    }
}
