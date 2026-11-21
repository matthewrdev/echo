using System.Security.Cryptography;
using System.Text;
using Echo.Models;
using Echo.Geometry;
using Echo.Infrastructure;

namespace Echo.Export;

public sealed class RedpointGeoreferenceWriter : IRedpointGeoreferenceWriter
{
    private readonly IFileOperations files;

    public RedpointGeoreferenceWriter(IFileOperations files)
    {
        this.files = files;
    }

    public void Write(string directory, GeoPosition origin, Surface surface)
    {
        var bounds = new ModelBounds(surface);
        var outsideLocalSite = Math.Abs(origin.Latitude) > 85 || bounds.HalfExtent(0) > 5000 || bounds.HalfExtent(2) > 5000;
        if (outsideLocalSite)
        {
            throw new EchoException("Redpoint calibration requires a local site within 85 degrees latitude and a footprint under 10 km.", 2);
        }
        using var model = File.OpenRead(Path.Combine(directory, "cliff.glb"));
        var result = new RedpointGeoreferenceExport
        {
            ModelSha256 = Convert.ToHexStringLower(SHA256.HashData(model)),
            VerticalDatum = origin.AltitudeDatum
        };
        // A rectangle samples the known transform even for a nearly vertical cliff.
        // It deliberately does not claim these generated positions are surveyed GCPs.
        var east = Math.Max(1, bounds.HalfExtent(0));
        var south = Math.Max(1, bounds.HalfExtent(2));
        var offsets = new double[][] { [-east, -south], [east, -south], [east, south], [-east, south], [0, 0] };
        foreach (var offset in offsets)
        {
            var local = new V3(bounds.Centre(0) + offset[0], bounds.Centre(1), bounds.Centre(2) + offset[1]);
            var gps = GeographicCoordinates.FromLocal(local, origin);
            result.GeoReference.ReferencePoints.Add(new RedpointReferencePoint
            {
                Id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"{result.ModelSha256}:{result.GeoReference.ReferencePoints.Count}"))[..16]).ToString(),
                Label = $"Echo calibration {result.GeoReference.ReferencePoints.Count + 1}",
                SceneX = local.X,
                SceneY = local.Y,
                SceneZ = local.Z,
                Latitude = gps.Latitude,
                Longitude = gps.Longitude,
                Elevation = GeographicCoordinates.HasEllipsoidalHeight(origin) ? gps.Altitude : null
            });
        }
        var points = result.GeoReference.ReferencePoints;
        var crossesBoundary = points.Max(point => point.Longitude) - points.Min(point => point.Longitude) > 1
            || points.Any(point => Math.Abs(point.Latitude) > 85);
        if (crossesBoundary)
        {
            throw new EchoException("This site crosses Redpoint's supported geographic bounds.", 2);
        }
        files.AtomicJson(Path.Combine(directory, "redpoint.georeference.json"), result);
    }
}
