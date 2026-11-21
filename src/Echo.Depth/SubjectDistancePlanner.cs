using Echo.Models;
using Echo.Geometry;

namespace Echo.Depth;

public static class SubjectDistancePlanner
{
    public static SubjectDistancePlan Estimate(ProjectData project, ReconstructionScene scene, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var settings = project.Settings;
        if (settings.SubjectDistanceMode == SubjectDistanceMode.Manual)
        {
            return new SubjectDistancePlan
            {
                Mode = SubjectDistanceMode.Manual,
                MaximumDistance = settings.SubjectDistanceLimit,
                Source = "manual subject distance"
            };
        }

        if (settings.SubjectDistanceMode == SubjectDistanceMode.Off || !settings.DepthRangeAutomatic)
        {
            return new SubjectDistancePlan
            {
                Mode = settings.SubjectDistanceMode,
                Source = settings.DepthRangeAutomatic ? "subject distance unrestricted" : "explicit depth range takes precedence"
            };
        }

        var extent = CaptureExtent(scene.Cameras, cancellation);
        var cameras = scene.Cameras.ToDictionary(camera => camera.ImageId, StringComparer.Ordinal);
        var photos = project.Images.ToDictionary(photo => photo.Id, StringComparer.Ordinal);
        var samples = scene.Cameras.ToDictionary(camera => camera.ImageId, _ => new List<double>(), StringComparer.Ordinal);
        // Deterministic, bounded sampling keeps planning independent of the number
        // of depth workers and avoids retaining a second full sparse cloud.
        var stride = Math.Max(1, (int)Math.Ceiling(scene.Points.Count / 100000.0));
        for (var index = 0; index < scene.Points.Count; index += stride)
        {
            cancellation.ThrowIfCancellationRequested();
            var point = scene.Points[index];
            var reliable = double.IsFinite(point.Error) && point.Error <= 4
                && DepthRangeEstimator.HasReliableTriangulation(point, cameras);
            if (!reliable)
            {
                continue;
            }

            AddObservation(point, point.ImageA, cameras, photos, samples);
            if (point.ImageB != point.ImageA)
            {
                AddObservation(point, point.ImageB, cameras, photos, samples);
            }
        }

        var foreground = new List<double>();
        foreach (var depths in samples.Values)
        {
            cancellation.ThrowIfCancellationRequested();
            if (depths.Count >= 10)
            {
                depths.Sort();
                foreground.Add(Quantile(depths, .2));
            }
        }

        foreground.Sort();
        var depth = foreground.Count == 0 ? 0 : Quantile(foreground, .5);
        // Camera extent protects wide and vertical flights; foreground evidence
        // protects a distant subject photographed from a compact camera track.
        // Equal camera votes prevent a highly textured background in a few views
        // from expanding the limit for the entire capture.
        var maximum = Math.Max(extent * 1.5, depth * 3);
        return new SubjectDistancePlan
        {
            Mode = SubjectDistanceMode.Auto,
            MaximumDistance = double.IsFinite(maximum) && maximum > 1e-8 ? maximum : null,
            CaptureExtent = extent,
            ForegroundDepth = depth,
            EvidenceCameras = foreground.Count,
            Source = foreground.Count == 0 ? "automatic capture footprint; no reliable foreground observations"
                : "automatic capture footprint and observed foreground depth"
        };
    }

    private static void AddObservation(CloudPoint point, string imageId,
        IReadOnlyDictionary<string, CameraPose> cameras, IReadOnlyDictionary<string, Photo> photos,
        IReadOnlyDictionary<string, List<double>> samples)
    {
        if (!cameras.TryGetValue(imageId, out var camera) || !photos.TryGetValue(imageId, out var photo))
        {
            return;
        }

        var pixel = new CameraProjector(photo, camera).Project(new V3(point.Position));
        if (!pixel.HasValue)
        {
            return;
        }

        var value = pixel.Value;
        var visible = value.X >= 0 && value.X < photo.Width && value.Y >= 0 && value.Y < photo.Height;
        var validDepth = double.IsFinite(value.Z) && value.Z > 0;
        if (visible && validDepth)
        {
            samples[imageId].Add(value.Z);
        }
    }

    private static double CaptureExtent(IReadOnlyList<CameraPose> cameras, CancellationToken cancellation)
    {
        var squared = 0.0;
        for (var axis = 0; axis < 3; axis++)
        {
            cancellation.ThrowIfCancellationRequested();
            var positions = cameras.Select(camera => camera.Centre[axis]).Where(double.IsFinite).Order().ToArray();
            if (positions.Length < 2)
            {
                continue;
            }

            var trim = positions.Length < 5 ? 0 : Math.Max(1, positions.Length / 20);
            var span = positions[positions.Length - 1 - trim] - positions[trim];
            squared += span * span;
        }

        return Math.Sqrt(squared);
    }

    private static double Quantile(IReadOnlyList<double> sorted, double fraction)
    {
        var position = (sorted.Count - 1) * fraction;
        var lower = (int)position;
        var upper = Math.Min(lower + 1, sorted.Count - 1);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }
}
