using Echo.Geometry;

namespace Echo.Fusion;

internal static class FusionDepthProjection
{
    public static FusionDepthRegion Project(DepthView view, FusionBounds bounds, V3 origin)
    {
        var minimum = new V3(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
        var maximum = new V3(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);
        var magnitude = (view.Centre - origin).Length;
        for (var corner = 0; corner < 8; corner++)
        {
            var world = new V3((corner & 1) == 0 ? bounds.Minimum.X : bounds.Maximum.X,
                (corner & 2) == 0 ? bounds.Minimum.Y : bounds.Maximum.Y, (corner & 4) == 0 ? bounds.Minimum.Z : bounds.Maximum.Z);
            magnitude = Math.Max(magnitude, (world - origin).Length);
            var local = CameraGeometry.Rotate(view.Inverse, world - view.Centre);
            minimum = new V3(Math.Min(minimum.X, local.X), Math.Min(minimum.Y, local.Y), Math.Min(minimum.Z, local.Z));
            maximum = new V3(Math.Max(maximum.X, local.X), Math.Max(maximum.Y, local.Y), Math.Max(maximum.Z, local.Z));
        }
        // Enclose float upload/rotation rounding as well as the double CPU path.
        // A block crossing the camera plane keeps the whole view conservatively.
        var guard = Math.Max(1e-5, magnitude * 1e-5);
        minimum -= new V3(guard, guard, guard);
        maximum += new V3(guard, guard, guard);
        if (maximum.Z <= 0)
        {
            return default;
        }

        var map = view.Map;
        if (!(minimum.Z > 0))
        {
            return new FusionDepthRegion(0, 0, map.Width, map.Height);
        }

        var left = Math.Min(minimum.X / minimum.Z, minimum.X / maximum.Z) * view.Focal + map.Width * .5;
        var right = Math.Max(maximum.X / minimum.Z, maximum.X / maximum.Z) * view.Focal + map.Width * .5;
        var top = Math.Min(minimum.Y / minimum.Z, minimum.Y / maximum.Z) * view.Focal + map.Height * .5;
        var bottom = Math.Max(maximum.Y / minimum.Z, maximum.Y / maximum.Z) * view.Focal + map.Height * .5;
        var outsideX = right < 0 || left >= map.Width - 1;
        var outsideY = bottom < 0 || top >= map.Height - 1;
        if (outsideX || outsideY)
        {
            return default;
        }

        var x = (int)Math.Clamp(Math.Floor(left) - 2, 0, map.Width - 1);
        var y = (int)Math.Clamp(Math.Floor(top) - 2, 0, map.Height - 1);
        var endX = (int)Math.Clamp(Math.Ceiling(right) + 3, 0, map.Width);
        var endY = (int)Math.Clamp(Math.Ceiling(bottom) + 3, 0, map.Height);
        return new FusionDepthRegion(x, y, endX - x, endY - y);
    }
}
