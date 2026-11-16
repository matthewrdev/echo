using Echo.Contracts;
using Echo.Geometry;

namespace Echo.Depth;

internal static class DepthCoverageGrid
{
    public static DepthCoverageSample[] Create(ReconstructionScene scene, Photo photo, CameraPose camera,
        DepthSearchRange range, DepthMap? prior, CancellationToken cancellation)
    {
        const int columns = 16;
        const int rows = 12;
        const int observationsPerCell = 9;
        var depths = new double[columns * rows * observationsPerCell];
        var distances = new double[depths.Length];
        var counts = new int[columns * rows];
        var cameras = scene.Cameras.ToDictionary(item => item.ImageId);
        var projector = new CameraProjector(photo, camera);
        foreach (var point in scene.Points)
        {
            cancellation.ThrowIfCancellationRequested();
            var observed = point.ImageA == photo.Id || point.ImageB == photo.Id;
            var reliable = observed && double.IsFinite(point.Error) && point.Error <= 4;
            if (!reliable || !DepthRangeEstimator.HasReliableTriangulation(point, cameras))
            {
                continue;
            }
            var pixel = projector.Project(new V3(point.Position));
            if (!pixel.HasValue)
            {
                continue;
            }
            var p = pixel.Value;
            var inside = p.X >= 0 && p.X < photo.Width && p.Y >= 0 && p.Y < photo.Height;
            var inRange = double.IsFinite(p.Z) && p.Z >= range.Near && p.Z <= range.Far;
            if (!inside || !inRange)
            {
                continue;
            }
            var x = p.X * columns / photo.Width;
            var y = p.Y * rows / photo.Height;
            var cell = (int)y * columns + (int)x;
            var distance = Math.Pow(x - (int)x - .5, 2) + Math.Pow(y - (int)y - .5, 2);
            Insert(depths, distances, counts, cell, p.Z, distance);
        }

        var samples = new List<DepthCoverageSample>(columns * rows * 3);
        var centre = new V3(camera.Centre);
        var focal = CameraGeometry.Focal(photo, camera);
        Span<double> local = stackalloc double[observationsPerCell];
        for (var y = 0; y < rows; y++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (var x = 0; x < columns; x++)
            {
                var u = (x + .5) / columns;
                var v = (y + .5) / rows;
                var ray = CameraGeometry.Rotate(camera.Rotation,
                    new V3((u - .5) * photo.Width / focal, (v - .5) * photo.Height / focal, 1));
                var depth = PriorDepth(prior, u, v, local);
                // Geometric overlap alone is not a successful photo match. If a
                // coarse region remains empty, try additional distinct viewpoints
                // on refinement, still requiring only the unchanged two good matches.
                var requiredViews = prior == null || depth > 0 ? 2 : prior.Width < 320 ? 3 : 4;
                var cell = y * columns + x;
                if (depth <= 0 && counts[cell] > 0)
                {
                    var values = depths.AsSpan(cell * observationsPerCell, counts[cell]);
                    values.Sort();
                    depth = values[values.Length / 2];
                }
                if (depth > 0)
                {
                    samples.Add(new DepthCoverageSample(centre + ray * depth, 1, requiredViews));
                }
                else
                {
                    // Unobserved regions still need coverage. Probe a depth band,
                    // rather than assuming the whole image is a single plane.
                    for (var index = 0; index < 3; index++)
                    {
                        var scale = index == 0 ? .8 : index == 1 ? 1 : 1.25;
                        depth = Math.Clamp(range.Median * scale, range.Near, range.Far);
                        samples.Add(new DepthCoverageSample(centre + ray * depth, 1.0 / 3, requiredViews));
                    }
                }
            }
        }
        return samples.ToArray();
    }

    private static void Insert(double[] depths, double[] distances, int[] counts, int cell, double depth, double distance)
    {
        var observationsPerCell = depths.Length / counts.Length;
        var offset = cell * observationsPerCell;
        var count = counts[cell];
        var index = count;
        while (index > 0 && distance < distances[offset + index - 1])
        {
            index--;
        }
        if (index >= observationsPerCell)
        {
            return;
        }
        var newCount = Math.Min(count + 1, observationsPerCell);
        for (var i = newCount - 1; i > index; i--)
        {
            depths[offset + i] = depths[offset + i - 1];
            distances[offset + i] = distances[offset + i - 1];
        }
        depths[offset + index] = depth;
        distances[offset + index] = distance;
        counts[cell] = newCount;
    }

    private static double PriorDepth(DepthMap? prior, double u, double v, Span<double> values)
    {
        if (prior == null)
        {
            return 0;
        }
        var x = (int)(u * prior.Width);
        var y = (int)(v * prior.Height);
        var count = 0;
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var sx = Math.Clamp(x + dx, 0, prior.Width - 1);
                var sy = Math.Clamp(y + dy, 0, prior.Height - 1);
                var depth = prior.DepthAt(sy * prior.Width + sx);
                if (float.IsFinite(depth) && depth > 0)
                {
                    values[count++] = depth;
                }
            }
        }
        if (count < 3)
        {
            return 0;
        }
        values[..count].Sort();
        return values[count / 2];
    }
}
