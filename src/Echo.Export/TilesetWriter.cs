using Echo.Models;
using Echo.Geometry;
using Echo.Infrastructure;

namespace Echo.Export;

public sealed class TilesetWriter : ITilesetWriter
{
    private readonly IFileOperations files;

    public TilesetWriter(IFileOperations files)
    {
        this.files = files;
    }

    public void Write(string directory, GeoPosition origin, Surface surface)
    {
        if (!GeographicCoordinates.HasEllipsoidalHeight(origin))
        {
            throw new EchoException("3D Tiles needs a known WGS84 ellipsoidal height for Earth-centred placement.", 2);
        }
        var bounds = new ModelBounds(surface);
        var m = CameraGeometry.LocalToEcefMatrix(origin);
        // 3D Tiles converts glTF Y-up to Z-up: (x,y,z) -> (x,-z,y).
        // Its column-major transform therefore maps tile ENU coordinates to ECEF.
        double[] transform = [m[0], m[4], m[8], 0, -m[2], -m[6], -m[10], 0, m[1], m[5], m[9], 0, m[3], m[7], m[11], 1];
        double[] box = [bounds.Centre(0), -bounds.Centre(2), bounds.Centre(1),
            bounds.HalfExtent(0) + 0.001, 0, 0, 0, bounds.HalfExtent(2) + 0.001, 0, 0, 0, bounds.HalfExtent(1) + 0.001];
        files.AtomicJson(Path.Combine(directory, "tileset.json"), new
        {
            asset = new
            {
                version = "1.1"
            },
            geometricError = 0,
            root = new
            {
                boundingVolume = new
                {
                    box
                },
                geometricError = 0,
                refine = "ADD",
                transform,
                content = new
                {
                    uri = "cliff.glb"
                }
            }
        });
    }
}
