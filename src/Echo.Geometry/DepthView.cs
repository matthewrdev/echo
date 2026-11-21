using Echo.Models;

namespace Echo.Geometry;

public sealed class DepthView
{

    public DepthView(Photo photo, CameraPose camera, DepthMap map)
    {
        var validDimensions = map.Width > 0 && map.Height > 0;
        if (!validDimensions || (long)map.Width * map.Height != map.SampleCount)
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

    public bool TrySurfacePoint(V3 world, double relativeTolerance, out V3 surface, bool preserveDepthEdges = false)
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
                var depth = Map.DepthAt((iy + dy) * Map.Width + ix + dx);
                if (w <= 0 || !(depth > 0))
                {
                    continue;
                }
                // Never average foreground and background into an invented surface.
                // Cloud validation may retain the agreeing side of an edge when
                // it carries at least half of the bilinear footprint below.
                if (!float.IsFinite(depth) || Math.Abs(depth - local.Z) >= tolerance)
                {
                    if (preserveDepthEdges)
                    {
                        continue;
                    }
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

    public double Difference(V3 world) => Difference(world, out _);

    public DepthObservation Observe(V3 world, double relativeTolerance)
    {
        var local = CameraGeometry.Rotate(Inverse, world - Centre);
        if (!(local.Z > 0) || !double.IsFinite(local.Z))
        {
            return default;
        }

        var x = local.X / local.Z * Focal + Map.Width * .5;
        var y = local.Y / local.Z * Focal + Map.Height * .5;
        var insideX = x >= 0 && x < Map.Width - 1;
        var insideY = y >= 0 && y < Map.Height - 1;
        if (!insideX || !insideY)
        {
            return default;
        }

        var ix = (int)x;
        var iy = (int)y;
        var minimum = double.PositiveInfinity;
        var maximum = double.NegativeInfinity;
        double sum = 0;
        double weight = 0;
        var complete = true;
        for (var dy = 0; dy <= 1; dy++)
        {
            for (var dx = 0; dx <= 1; dx++)
            {
                var depth = Map.DepthAt((iy + dy) * Map.Width + ix + dx);
                if (!(depth > 0) || !float.IsFinite(depth))
                {
                    complete = false;
                    continue;
                }

                minimum = Math.Min(minimum, depth);
                maximum = Math.Max(maximum, depth);
                var w = (dx == 0 ? 1 - (x - ix) : x - ix) * (dy == 0 ? 1 - (y - iy) : y - iy);
                sum += depth * w;
                weight += w;
            }
        }

        var tolerance = Math.Max(local.Z * relativeTolerance, local.Z / Focal * 2);
        // Interpolation across a depth discontinuity cannot establish either a
        // surface or empty space. Partial samples can support a surface, but
        // only a fully valid neighbourhood can supply free-space evidence.
        if (weight < .5 || maximum - minimum > tolerance * 2)
        {
            return default;
        }

        return new DepthObservation(sum / weight - local.Z, local.Z, complete);
    }

    public double Difference(V3 world, out double cameraDepth)
    {
        var p = CameraGeometry.Rotate(Inverse, world - Centre);
        cameraDepth = p.Z;
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
                var depth = Map.DepthAt((iy + dy) * Map.Width + ix + dx);
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
