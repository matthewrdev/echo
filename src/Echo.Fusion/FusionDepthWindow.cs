using Echo.Geometry;

namespace Echo.Fusion;

internal readonly record struct FusionDepthWindow(DepthView View, FusionDepthRegion Region, int Offset)
{
    public DepthObservation Observe(V3 world, double relativeTolerance, ReadOnlySpan<float> depths)
    {
        var local = CameraGeometry.Rotate(View.Inverse, world - View.Centre);
        if (!(local.Z > 0) || !double.IsFinite(local.Z))
        {
            return default;
        }
        var x = local.X / local.Z * View.Focal + View.Map.Width * .5;
        var y = local.Y / local.Z * View.Focal + View.Map.Height * .5;
        var insideX = x >= Region.Left && x < Region.Left + Region.Width - 1;
        var insideY = y >= Region.Top && y < Region.Top + Region.Height - 1;
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
        for (var dy = 0; dy < 2; dy++)
        {
            for (var dx = 0; dx < 2; dx++)
            {
                var depth = depths[Offset + (iy + dy - Region.Top) * Region.Width + ix + dx - Region.Left];
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
        var tolerance = Math.Max(local.Z * relativeTolerance, local.Z / View.Focal * 2);
        if (weight < .5 || maximum - minimum > tolerance * 2)
        {
            return default;
        }
        return new DepthObservation(sum / weight - local.Z, local.Z, complete);
    }
}
