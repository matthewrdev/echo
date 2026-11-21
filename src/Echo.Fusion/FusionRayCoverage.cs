using Echo.Geometry;
using Echo.Models;

namespace Echo.Fusion;

internal static class FusionRayCoverage
{
    public static bool Add(DenseSample point, DepthView?[] sources, double relativeTolerance, double voxel,
        long limit, FusionGridBatch batch, CancellationToken cancellation)
    {
        var finite = float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);
        if (!finite)
        {
            throw new EchoException("The dense cloud contains a non-finite position.", 5);
        }

        var validSource = point.View >= 0 && point.View < sources.Length;
        if (!validSource || sources[point.View] is not { } view)
        {
            throw new EchoException("A dense cloud sample has no source depth view.", 5);
        }

        var world = new V3(point.X, point.Y, point.Z);
        var ray = world - view.Centre;
        var cameraDepth = CameraGeometry.Rotate(view.Inverse, ray).Z;
        var invalidRay = !(cameraDepth > 0) || !double.IsFinite(cameraDepth)
            || !(view.Focal > 0) || !double.IsFinite(view.Focal);
        if (invalidRay)
        {
            throw new EchoException("A dense cloud sample has an invalid source-camera ray.", 5);
        }

        var uncertainty = FusionDepthUncertainty.Radius(cameraDepth, view.Focal, relativeTolerance);
        var extent = ray * (uncertainty / cameraDepth);
        var longestAxis = Math.Max(Math.Abs(extent.X), Math.Max(Math.Abs(extent.Y), Math.Abs(extent.Z)));
        var steps = Math.Ceiling(longestAxis / voxel);
        if (!double.IsFinite(steps) || steps > Math.Min(limit, int.MaxValue))
        {
            return false;
        }

        var stepCount = Math.Max(1, (int)steps);
        var step = extent / stepCount;
        // Retain the full accepted depth uncertainty, including negative cells.
        // A long loop uses Int64 to avoid overflow at the supported grid limit.
        for (long offset = -stepCount; offset <= stepCount; offset++)
        {
            if (offset % 4096 == 0)
            {
                cancellation.ThrowIfCancellationRequested();
            }

            var sample = world + step * offset;
            batch.Add(new Grid(Coordinate(sample.X, voxel), Coordinate(sample.Y, voxel), Coordinate(sample.Z, voxel)));
        }

        return true;
    }

    private static int Coordinate(double value, double voxel)
    {
        var coordinate = Math.Floor(value / voxel);
        var outsideRange = !double.IsFinite(coordinate) || coordinate < int.MinValue + 2.0 || coordinate > int.MaxValue - 2.0;
        if (outsideRange)
        {
            throw new EchoException("Fusion coordinates exceed the supported grid range.", 5);
        }

        return (int)coordinate;
    }
}
