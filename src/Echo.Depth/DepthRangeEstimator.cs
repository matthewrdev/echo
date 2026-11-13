using System.Buffers;
using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Geometry;

namespace Echo.Depth;

[Export(typeof(IDepthRangeEstimator)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class DepthRangeEstimator : IDepthRangeEstimator
{
    private readonly ICameraGeometry geometry;

    [ImportingConstructor]
    public DepthRangeEstimator(ICameraGeometry geometry)
    {
        this.geometry = geometry;
    }

    public DepthSearchRange Estimate(ProjectData project, ReconstructionScene scene, Photo photo, CameraPose camera, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var settings = project.Settings;
        if (!settings.DepthRangeAutomatic)
        {
            return new DepthSearchRange(settings.DepthNear, settings.DepthFar, (settings.DepthNear + settings.DepthFar) * .5, 0, "manual");
        }

        if (scene.Points.Count < 10)
        {
            return FromCameraBounds(scene, cancellation);
        }

        // One projection pass, no per-tile lists or growth. Each concurrent image
        // leases its own buffers; only populated spans are read or sorted.
        var capacity = scene.Points.Count;
        var depths = ArrayPool<double>.Shared.Rent(checked(capacity * 2));
        try
        {
            var tiles = ArrayPool<byte>.Shared.Rent(capacity);
            try
            {
                return FromScene(scene, photo, camera, depths.AsSpan(0, capacity),
                    depths.AsSpan(capacity, capacity), tiles.AsSpan(0, capacity), cancellation);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(tiles);
            }
        }
        finally
        {
            ArrayPool<double>.Shared.Return(depths);
        }
    }

    private DepthSearchRange FromScene(ReconstructionScene scene, Photo photo, CameraPose camera,
        Span<double> depths, Span<double> grouped, Span<byte> tiles, CancellationToken cancellation)
    {
        Span<int> observedCounts = stackalloc int[48];
        Span<int> visibleCounts = stackalloc int[48];
        observedCounts.Clear();
        visibleCounts.Clear();
        var count = 0;
        var observedCount = 0;
        var projector = new CameraProjector(geometry, photo, camera);
        foreach (var point in scene.Points)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!double.IsFinite(point.Error) || point.Error > 4)
            {
                continue;
            }

            var pixel = projector.Project(new V3(point.Position));
            if (!pixel.HasValue)
            {
                continue;
            }

            var projected = pixel.Value;
            var insideImage = projected.X >= 0 && projected.X < photo.Width
                && projected.Y >= 0 && projected.Y < photo.Height;
            if (!insideImage || !double.IsFinite(projected.Z))
            {
                continue;
            }

            var tile = (int)(projected.Y * 6 / photo.Height) * 8 + (int)(projected.X * 8 / photo.Width);
            var observed = point.ImageA == photo.Id || point.ImageB == photo.Id;
            depths[count] = projected.Z;
            tiles[count] = (byte)(tile | (observed ? 64 : 0));
            count++;
            visibleCounts[tile]++;
            if (observed)
            {
                observedCounts[tile]++;
                observedCount++;
            }
        }

        if (count < 10)
        {
            return FromCameraBounds(scene, cancellation);
        }

        var useObserved = observedCount >= 10;
        var counts = useObserved ? observedCounts : visibleCounts;
        Span<int> offsets = stackalloc int[48];
        var sampleCount = 0;
        for (var tile = 0; tile < counts.Length; tile++)
        {
            offsets[tile] = sampleCount;
            sampleCount += counts[tile];
        }

        for (var index = 0; index < count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var tile = tiles[index];
            if (!useObserved || (tile & 64) != 0)
            {
                grouped[offsets[tile & 63]++] = depths[index];
            }
        }

        var samples = depths[..sampleCount];
        grouped[..sampleCount].CopyTo(samples);
        // Sample each image region independently: a densely matched foreground
        // must not trim away a smaller, more distant section of the same photo.
        var source = useObserved ? "automatic: observed scene points" : "automatic: scene points in camera view";
        return FromTiles(samples, grouped[..sampleCount], counts, source, cancellation);
    }

    private DepthSearchRange FromTiles(Span<double> samples, Span<double> grouped, ReadOnlySpan<int> counts,
        string source, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        samples.Sort();
        var globalTrim = Math.Max(1, samples.Length / 100);
        var near = samples[globalTrim] * .75;
        var far = samples[samples.Length - 1 - globalTrim] * 1.25;
        var offset = 0;
        foreach (var count in counts)
        {
            cancellation.ThrowIfCancellationRequested();
            var tile = grouped.Slice(offset, count);
            offset += count;
            if (count < 3)
            {
                continue;
            }

            tile.Sort();
            var trim = count / 50;
            near = Math.Min(near, tile[trim] * .75);
            far = Math.Max(far, tile[count - 1 - trim] * 1.25);
        }

        return new DepthSearchRange(near, far, samples[samples.Length / 2], samples.Length, source);
    }

    private DepthSearchRange FromCameraBounds(ReconstructionScene scene, CancellationToken cancellation)
    {
        var minimum = new V3(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
        var maximum = new V3(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);
        foreach (var camera in scene.Cameras)
        {
            cancellation.ThrowIfCancellationRequested();
            var centre = new V3(camera.Centre);
            minimum = new V3(Math.Min(minimum.X, centre.X), Math.Min(minimum.Y, centre.Y), Math.Min(minimum.Z, centre.Z));
            maximum = new V3(Math.Max(maximum.X, centre.X), Math.Max(maximum.Y, centre.Y), Math.Max(maximum.Z, centre.Z));
        }

        var extent = (maximum - minimum).Length;
        if (!double.IsFinite(extent) || extent <= 1e-8)
        {
            throw new EchoException("Cannot infer a depth range from the aligned scene. Set a manual near/far range in Depth settings.", 5);
        }

        // Without reliable scene observations, the capture footprint supplies a
        // scale-dependent search interval, not a fixed distance ceiling.
        return new DepthSearchRange(extent * .02, extent * 4, extent, 0, "automatic: camera bounds fallback; no reliable scene coverage");
    }
}
