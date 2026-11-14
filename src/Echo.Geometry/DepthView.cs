using Echo.Contracts;

namespace Echo.Geometry;

public sealed class DepthView
{

    public DepthView(Photo photo, CameraPose camera, DepthMap map)
    {
        var validDimensions = map.Width > 0 && map.Height > 0;
        if (!validDimensions || (long)map.Width * map.Height != map.Depth.Length)
        {
            throw new EchoException($"Invalid depth-map dimensions or sample count for image {map.ImageId}.", 5);
        }
        Projection = new CameraProjector(photo, camera);
        Map = map;
        Camera = camera;
        Centre = new V3(camera.Centre);
        Inverse = CameraGeometry.Transpose(camera.Rotation);
        Focal = CameraGeometry.Focal(photo, camera) * map.Width / photo.Width;
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

    public V3 Point(int x, int y, double depth) => Centre + CameraGeometry.Rotate(Camera.Rotation, new V3((x - Map.Width * .5) / Focal, (y - Map.Height * .5) / Focal, 1)) * depth;

    public bool TrySurfacePoint(V3 world, double relativeTolerance, out V3 surface)
    {
        surface = default;
        var local = CameraGeometry.Rotate(Inverse, world - Centre);
        if (!(local.Z > 0) || !double.IsFinite(local.Z))
        {
            return false;
        }
        var x = local.X / local.Z * Focal + Map.Width * .5;
        var y = local.Y / local.Z * Focal + Map.Height * .5;
        var insideX = x >= 0 && x < Map.Width - 1;
        var insideY = y >= 0 && y < Map.Height - 1;
        if (!insideX || !insideY)
        {
            return false;
        }
        var ix = (int)x;
        var iy = (int)y;
        var tolerance = Math.Max(local.Z / Focal * 2, local.Z * relativeTolerance);
        double sum = 0;
        double weight = 0;
        for (var dy = 0; dy <= 1; dy++)
        {
            for (var dx = 0; dx <= 1; dx++)
            {
                var w = (dx == 0 ? 1 - (x - ix) : x - ix) * (dy == 0 ? 1 - (y - iy) : y - iy);
                var depth = Map.Depth[(iy + dy) * Map.Width + ix + dx];
                if (w <= 0 || !(depth > 0))
                {
                    continue;
                }
                // Do not interpolate a fictitious surface between foreground and
                // background depths. Every contributing valid corner must agree.
                if (!float.IsFinite(depth) || Math.Abs(depth - local.Z) >= tolerance)
                {
                    return false;
                }
                sum += depth * w;
                weight += w;
            }
        }
        if (weight < .5)
        {
            return false;
        }
        surface = Centre + (world - Centre) * (sum / weight / local.Z);
        return true;
    }

    public bool ReprojectsTo(V3 world, int x, int y, double pixelTolerance)
    {
        var local = CameraGeometry.Rotate(Inverse, world - Centre);
        if (!(local.Z > 0))
        {
            return false;
        }
        var dx = local.X / local.Z * Focal + Map.Width * .5 - x;
        var dy = local.Y / local.Z * Focal + Map.Height * .5 - y;
        return dx * dx + dy * dy <= pixelTolerance * pixelTolerance;
    }

    public double Difference(V3 world)
    {
        var p = CameraGeometry.Rotate(Inverse, world - Centre);
        if (!(p.Z > 0) || !double.IsFinite(p.Z))
        {
            return double.NaN;
        }

        var x = p.X / p.Z * Focal + Map.Width * .5;
        var y = p.Y / p.Z * Focal + Map.Height * .5;
        // Check in floating point first: near the camera plane, projection can
        // exceed Int32.MaxValue and overflow an integer ix + 1 bounds check.
        var insideX = x >= 0 && x < Map.Width - 1;
        var insideY = y >= 0 && y < Map.Height - 1;
        if (!insideX || !insideY)
        {
            return double.NaN;
        }

        var ix = (int)x;
        var iy = (int)y;
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
