using Echo.Contracts;
using Echo.Geometry;

namespace Echo.Fusion;

internal static class FusionGridAxes
{
    public static GridAxis Select(DenseCloudData cloud, DepthView?[] sources, CancellationToken cancellation)
    {
        var weights = new V3(0, 0, 0);
        var stride = Math.Max(1, cloud.Samples.Count / 4096);
        for (long index = 0; index < cloud.Samples.Count; index += stride)
        {
            cancellation.ThrowIfCancellationRequested();
            var point = cloud.Samples[(int)index];
            var validSource = point.View >= 0 && point.View < sources.Length;
            if (!validSource || sources[point.View] is not { } view)
            {
                continue;
            }
            var ray = new V3(point.X, point.Y, point.Z) - view.Centre;
            var length = ray.Length;
            if (length > 0 && double.IsFinite(length))
            {
                weights += new V3(Math.Abs(ray.X), Math.Abs(ray.Y), Math.Abs(ray.Z)) / length;
            }
        }
        if (weights.X > weights.Y && weights.X > weights.Z)
        {
            return GridAxis.X;
        }
        return weights.Y > weights.Z ? GridAxis.Y : GridAxis.Z;
    }

    public static Grid ToStorage(Grid cell, GridAxis axis) => axis switch
    {
        GridAxis.X => new Grid(cell.Y, cell.Z, cell.X),
        GridAxis.Y => new Grid(cell.X, cell.Z, cell.Y),
        _ => cell
    };

    public static Grid ToWorld(Grid cell, GridAxis axis) => axis switch
    {
        GridAxis.X => new Grid(cell.Z, cell.X, cell.Y),
        GridAxis.Y => new Grid(cell.X, cell.Z, cell.Y),
        _ => cell
    };
}
