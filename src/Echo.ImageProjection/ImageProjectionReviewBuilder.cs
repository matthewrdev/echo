using Echo.Geometry;
using Echo.Meshing;
using Echo.Models;

namespace Echo.ImageProjection;

internal static class ImageProjectionReviewBuilder
{
    public static ImageProjectionReview Build(Photo image, CameraPose pose, IReadOnlyList<ProjectedImageFeature> features,
        IMeshRaycaster mesh, CancellationToken cancellation)
    {
        var camera = new CameraProjector(image, pose);
        var origin = new V3(pose.Centre);
        var result = new List<ImageProjectionReviewFeature>(features.Count);
        foreach (var feature in features)
        {
            var samples = new List<ImageProjectionReviewSample?>(feature.Samples.Count);
            foreach (var sample in feature.Samples)
            {
                cancellation.ThrowIfCancellationRequested();
                if (sample.Position == null)
                {
                    samples.Add(null);
                    continue;
                }

                var point = new V3(sample.Position);
                var projected = camera.Project(point);
                if (projected is not { } pixel || !InFrame(pixel, image))
                {
                    samples.Add(null);
                    continue;
                }

                double[] coordinates = [pixel.X, pixel.Y];
                var ray = CameraGeometry.Rotate(pose.Rotation, CameraGeometry.Ray(image, coordinates, pose));
                var hit = mesh.FirstHit(origin, ray);
                var distance = hit != null ? (double?)(hit.Value.Position - point).Length : null;
                samples.Add(new ImageProjectionReviewSample(coordinates, distance));
            }

            result.Add(new ImageProjectionReviewFeature(feature.Id, samples));
        }

        return new ImageProjectionReview(image.Id, image.Width, image.Height, result);
    }

    private static bool InFrame(V3 pixel, Photo image) =>
        pixel.X >= 0 && pixel.X < image.Width && pixel.Y >= 0 && pixel.Y < image.Height;
}
