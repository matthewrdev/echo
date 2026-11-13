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

        var observed = Enumerable.Range(0, 48).Select(_ => new List<double>()).ToArray();
        var visible = Enumerable.Range(0, 48).Select(_ => new List<double>()).ToArray();
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
            visible[tile].Add(projected.Z);
            if (point.ImageA == photo.Id || point.ImageB == photo.Id)
            {
                observed[tile].Add(projected.Z);
            }
        }

        // Sample each image region independently: a densely matched foreground
        // must not trim away a smaller, more distant section of the same photo.
        var range = FromTiles(observed, "automatic: observed scene points")
            ?? FromTiles(visible, "automatic: scene points in camera view");
        return range ?? FromCameraBounds(scene, cancellation);
    }

    private DepthSearchRange? FromTiles(List<double>[] tiles, string source)
    {
        var samples = tiles.SelectMany(tile => tile).Order().ToArray();
        if (samples.Length < 10)
        {
            return null;
        }

        var globalTrim = Math.Max(1, samples.Length / 100);
        var near = samples[globalTrim] * .75;
        var far = samples[samples.Length - 1 - globalTrim] * 1.25;
        foreach (var tile in tiles)
        {
            if (tile.Count < 3)
            {
                continue;
            }

            tile.Sort();
            var trim = tile.Count / 50;
            near = Math.Min(near, tile[trim] * .75);
            far = Math.Max(far, tile[tile.Count - 1 - trim] * 1.25);
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
