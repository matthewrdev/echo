using Echo.Geometry;
using Echo.Models;

namespace Echo.Pipeline.Exporting;

internal static class GeoreferenceSidecar
{
    public static object Create(string frame, GeoPosition? origin, IEnumerable<CameraPose> cameras, IEnumerable<string> warnings)
    {
        return new
        {
            schemaVersion = 1,
            frame,
            origin,
            referenced = origin != null,
            localToEcefRowMajor = origin == null ? null : CameraGeometry.LocalToEcefMatrix(origin),
            axisConvention = "X east, Y up, Z south",
            geographicDatum = "WGS84 horizontal",
            verticalDatum = origin?.AltitudeDatum ?? "unreferenced",
            formula = "Local tangent ECEF rotation around origin; stored model coordinates are East/Up/South. Unknown altitude datum precludes verified absolute elevation.",
            cameras,
            warnings
        };
    }
}
