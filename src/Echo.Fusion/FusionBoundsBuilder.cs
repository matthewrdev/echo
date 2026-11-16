using Echo.Geometry;

namespace Echo.Fusion;

internal static class FusionBoundsBuilder
{
    public static FusionBounds Measure(ReadOnlySpan<Grid> cells, double voxel)
    {
        var minimum = new V3(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
        var maximum = new V3(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);
        foreach (var cell in cells)
        {
            minimum = new V3(Math.Min(minimum.X, cell.X * voxel), Math.Min(minimum.Y, cell.Y * voxel), Math.Min(minimum.Z, cell.Z * voxel));
            maximum = new V3(Math.Max(maximum.X, cell.X * voxel), Math.Max(maximum.Y, cell.Y * voxel), Math.Max(maximum.Z, cell.Z * voxel));
        }
        return new FusionBounds(minimum, maximum);
    }
}
