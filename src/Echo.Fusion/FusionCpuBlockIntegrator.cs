using Echo.Geometry;

namespace Echo.Fusion;

internal static class FusionCpuBlockIntegrator
{
    public static void Integrate(ReadOnlySpan<Grid> cells, Span<FusionAccumulator> accumulators,
        ReadOnlySpan<FusionDepthWindow> windows, ReadOnlySpan<float> depths, double voxel, double tolerance, V3 origin)
    {
        var bounds = FusionBoundsBuilder.Measure(cells, voxel);
        // Tile batches contain at most 128 windows. Keep the camera shortlist on
        // the worker stack, in original order, and retain all overlapping tiles.
        Span<int> selected = stackalloc int[windows.Length];
        var count = 0;
        for (var index = 0; index < windows.Length; index++)
        {
            var window = windows[index];
            var projected = FusionDepthProjection.Project(window.View, bounds, origin);
            var region = window.Region;
            var overlapsX = projected.Left < region.Left + region.Width - 1 && projected.Left + projected.Width - 1 > region.Left;
            var overlapsY = projected.Top < region.Top + region.Height - 1 && projected.Top + projected.Height - 1 > region.Top;
            var valid = projected.Width >= 2 && projected.Height >= 2;
            var overlaps = overlapsX && overlapsY;
            if (valid && overlaps)
            {
                selected[count++] = index;
            }
        }

        for (var index = 0; index < cells.Length; index++)
        {
            var key = cells[index];
            var world = new V3(key.X * voxel, key.Y * voxel, key.Z * voxel);
            ref var accumulator = ref accumulators[index];
            foreach (var selectedIndex in selected[..count])
            {
                var window = windows[selectedIndex];
                accumulator.Add(window.Observe(world, tolerance, depths), window.View.Focal, tolerance, voxel);
            }
        }
    }
}
