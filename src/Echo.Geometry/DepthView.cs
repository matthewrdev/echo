using Echo.Contracts;

namespace Echo.Geometry;

public sealed class DepthView
{
    private readonly ICameraGeometry cameraGeometry;

    public DepthView(ICameraGeometry cameraGeometry, Photo photo, CameraPose camera, DepthMap map)
    {
        this.cameraGeometry = cameraGeometry;
        Projection = new CameraProjector(cameraGeometry, photo, camera);
        Map = map;
        Camera = camera;
        Centre = new V3(camera.Centre);
        Inverse = cameraGeometry.Transpose(camera.Rotation);
        Focal = cameraGeometry.Focal(photo, camera) * map.Width / photo.Width;
    }

    public CameraProjector Projection
    {
        get;
    }

    public DepthMap Map
    {
        get;
    }
    public CameraPose Camera
    {
        get;
    }
    public V3 Centre
    {
        get;
    }
    public double[] Inverse
    {
        get;
    }
    public double Focal
    {
        get;
    }

    public V3 Point(int x, int y, double depth) => Centre + cameraGeometry.Rotate(Camera.Rotation, new V3((x - Map.Width * .5) / Focal, (y - Map.Height * .5) / Focal, 1)) * depth;

    public double Difference(V3 world)
    {
        var p = cameraGeometry.Rotate(Inverse, world - Centre);
        if (p.Z <= 0)
        {
            return double.NaN;
        }

        var x = p.X / p.Z * Focal + Map.Width * .5;
        var y = p.Y / p.Z * Focal + Map.Height * .5;
        var ix = (int)Math.Floor(x);
        var iy = (int)Math.Floor(y);
        if (ix < 0 || iy < 0 || ix + 1 >= Map.Width || iy + 1 >= Map.Height)
        {
            return double.NaN;
        }

        double sum = 0;
        double weight = 0;
        for (var dy = 0; dy <= 1; dy++)
        {
            for (var dx = 0; dx <= 1; dx++)
            {
                var depth = Map.Depth[(iy + dy) * Map.Width + ix + dx];
                if (depth <= 0)
                {
                    continue;
                }

                var w = (dx == 0 ? 1 - (x - ix) : x - ix) * (dy == 0 ? 1 - (y - iy) : y - iy);
                sum += depth * w;
                weight += w;
            }
        }

        return weight < .5 ? double.NaN : sum / weight - p.Z;
    }
}
