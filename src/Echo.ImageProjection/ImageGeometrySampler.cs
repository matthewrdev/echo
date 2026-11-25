using Echo.Geometry;
using Echo.Meshing;
using Echo.Models;

namespace Echo.ImageProjection;

internal static class ImageGeometrySampler
{
    public static List<ProjectedImageFeature> Project(ImageGeometryRequest request, Photo photo, RegistrationResult camera,
        IMeshRaycaster mesh, CancellationToken cancellation)
    {
        var results = new List<ProjectedImageFeature>(request.Document.Features.Count);
        const int maximumSamples = 1_000_000;
        var remaining = maximumSamples;
        var pose = camera.Fit.Pose;
        var origin = new V3(pose.Centre);
        var minimumDepth = camera.Fit.MinimumDepth ?? 1e-8;
        foreach (var feature in request.Document.Features)
        {
            var samples = new List<ImageGeometrySample>();
            MeshRayHit? previous = null;
            var gap = false;
            var miss = false;
            var weakSupport = false;
            foreach (var pixel in SamplePixels(feature, request.SampleStep, maximumSamples))
            {
                cancellation.ThrowIfCancellationRequested();
                if (remaining-- <= 0)
                {
                    throw new EchoException("The geometry exceeds one million samples. Increase --sample-step.", 2);
                }

                var direction = CameraGeometry.Rotate(pose.Rotation, CameraGeometry.Ray(photo, pixel, pose));
                var hit = mesh.FirstHit(origin, direction, minimumDepth);
                var discontinuity = IsDiscontinuity(previous, hit, request.MaximumJump);
                var breakBefore = previous == null || hit == null || discontinuity;
                samples.Add(new ImageGeometrySample(pixel, hit?.Position.Array, hit?.Normal.Array, breakBefore));
                gap |= discontinuity;
                miss |= hit == null;
                weakSupport = weakSupport || HasWeakSupport(camera.Support, pixel, photo.Width * .05);
                previous = hit;
            }

            var flags = new List<string>();
            if (miss)
            {
                flags.Add("mesh-miss");
            }

            if (gap)
            {
                flags.Add("surface-discontinuity");
            }

            if (weakSupport)
            {
                flags.Add("sparse-camera-support");
            }

            results.Add(new ProjectedImageFeature(feature.Id, feature.Geometry, samples, flags));
        }

        return results;
    }

    private static bool IsDiscontinuity(MeshRayHit? previous, MeshRayHit? current, double? maximumJump)
    {
        if (previous is not { } first || current is not { } next)
        {
            return false;
        }

        var tolerance = maximumJump ?? Math.Min(first.Depth, next.Depth) * .025;
        return (first.Position - next.Position).Length > tolerance;
    }

    private static bool HasWeakSupport(IReadOnlyList<RegistrationMatch> support, double[] pixel, double distance) =>
        support.Count > 0 && support.All(match => CameraGeometry.Square(pixel[0] - match.Pixel[0])
            + CameraGeometry.Square(pixel[1] - match.Pixel[1]) > distance * distance);

    private static IEnumerable<double[]> SamplePixels(ImageGeometryFeature feature, double step, int maximumSamples)
    {
        var points = feature.Pixels;
        var closed = feature.Geometry == "polygon" && !points[0].SequenceEqual(points[^1]);
        var edges = points.Count - 1 + (closed ? 1 : 0);
        for (var index = 0; index < edges; index++)
        {
            var a = points[index];
            var b = points[(index + 1) % points.Count];
            var dx = b[0] - a[0];
            var dy = b[1] - a[1];
            var segments = Math.Ceiling(Math.Sqrt(dx * dx + dy * dy) / step);
            if (segments > maximumSamples)
            {
                throw new EchoException("An image edge exceeds one million samples. Increase --sample-step.", 2);
            }

            for (var offset = 0; offset < segments; offset++)
            {
                var fraction = offset / segments;
                yield return [a[0] + fraction * dx, a[1] + fraction * dy];
            }
        }

        yield return closed ? points[0] : points[^1];
    }
}
