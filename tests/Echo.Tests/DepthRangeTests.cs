using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Depth;
using Echo.Imaging;
using Echo.Projects;
using SkiaSharp;

namespace Echo.Tests;

[Export]
public sealed class DepthRangeTests
{
    private readonly IDepthEstimator estimator;
    private readonly IProjectStoreFactory stores;
    private readonly IRasterImageService images;

    [ImportingConstructor]
    public DepthRangeTests(IDepthEstimator estimator, IProjectStoreFactory stores, IRasterImageService images)
    {
        this.estimator = estimator;
        this.stores = stores;
        this.images = images;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Nearly stationary camera pairs cannot collapse the automatic depth interval", () =>
        {
            var photo = new Photo { Id = "reference", Width = 800, Height = 600, FocalPixels = 600 };
            var camera = new CameraPose { ImageId = photo.Id };
            var scene = new ReconstructionScene
            {
                Cameras =
                [
                    camera,
                    new CameraPose { ImageId = "other", Centre = [5, 0, 0] },
                    new CameraPose { ImageId = "stationary", Centre = [.000001, 0, 0] }
                ]
            };
            var project = new ProjectData { Images = [photo] };
            for (var index = 0; index < 30; index++)
            {
                scene.Points.Add(Point(photo.Id, -20 + index, 0, 100));
            }
            var clean = DepthRangeEstimator.Estimate(project, scene, photo, camera, default);
            for (var index = 0; index < 12; index++)
            {
                // A separate image tile with several tiny-baseline observations
                // used to override global trimming despite almost no parallax.
                scene.Points.Add(Point(photo.Id, .02, .02, .1, "stationary"));
            }
            scene.Points.Add(Point(photo.Id, 0, 0, .001, "missing"));
            scene.Points.Add(Point(photo.Id, 0, 0, .001, photo.Id));
            var filtered = DepthRangeEstimator.Estimate(project, scene, photo, camera, default);
            Check(filtered.Near == clean.Near && filtered.Far == clean.Far && filtered.Median == clean.Median,
                "Weak and missing-camera observations must not starve the real surface of search samples");
            Check(filtered.SampleCount == 30 && filtered.RejectedObservations == 14, "Report excluded observations");

            scene.Cameras.Add(new CameraPose { ImageId = "close", Centre = [.02, 0, 0] });
            for (var index = 0; index < 12; index++)
            {
                scene.Points.Add(Point(photo.Id, .02, .02, .1, "close"));
            }
            var nearby = DepthRangeEstimator.Estimate(project, scene, photo, camera, default);
            Check(nearby.Near < .1 && nearby.Far == clean.Far,
                "Genuinely close geometry with strong parallax must remain in the range; no metric near clamp");

            scene.Points = scene.Points.Skip(30).ToList();
            scene.Points = scene.Points.Take(14).Concat(scene.Points.Skip(26)).ToList();
            var fallback = DepthRangeEstimator.Estimate(project, scene, photo, camera, default);
            Check(fallback.SampleCount == 0 && fallback.Source.Contains("fallback"),
                "All-weak observations must fall back to capture bounds");
        });

        tests.Run("Automatic depth includes a distant image region, rejects outliers and scales with the scene", () =>
        {
            var photo = new Photo { Id = "reference", Width = 800, Height = 600, FocalPixels = 600 };
            var project = new ProjectData { Images = [photo] };
            var camera = new CameraPose { ImageId = photo.Id };
            var scene = new ReconstructionScene { Cameras = [camera, new CameraPose { ImageId = "other", Centre = [5, 0, 0] }] };
            for (var index = 0; index < 1000; index++)
            {
                scene.Points.Add(Point(photo.Id, -15, 0, 80));
            }
            for (var index = 0; index < 12; index++)
            {
                scene.Points.Add(Point(photo.Id, 100, index * .1, 220));
            }
            scene.Points.Add(Point(photo.Id, -15000, 0, 80000));
            scene.Points.Add(Point(photo.Id, 0, 0, -100));
            scene.Points.Add(Point(photo.Id, 500, 0, 100));
            scene.Points.Add(new CloudPoint { ImageA = photo.Id, Position = [0, 0, 9000], Error = 50 });
            var range = DepthRangeEstimator.Estimate(project, scene, photo, camera, default);
            Check(range.Near < 80 && range.Far > 220, "Both near and distant regions must be searched");
            Check(range.Far < 400, "Outliers, points behind the camera and points outside its view must not expand the interval");
            foreach (var point in scene.Points)
            {
                point.Position = point.Position.Select(value => value * 10).ToArray();
            }
            foreach (var pose in scene.Cameras)
            {
                pose.Centre = pose.Centre.Select(value => value * 10).ToArray();
            }
            var scaled = DepthRangeEstimator.Estimate(project, scene, photo, camera, default);
            Check(Math.Abs(scaled.Near - range.Near * 10) < 1e-8, "Near range follows scene scale");
            Check(Math.Abs(scaled.Far - range.Far * 10) < 1e-8, "Far range has no fixed metric ceiling");
            project.Settings.DepthRangeAutomatic = false;
            project.Settings.DepthNear = 7;
            project.Settings.DepthFar = 6000;
            var manual = DepthRangeEstimator.Estimate(project, scene, photo, camera, default);
            Check(manual.Near == 7 && manual.Far == 6000, "Manual bounds are used exactly, without percentile narrowing");
        });

        tests.Run("Automatic depth fallback scales to the capture bounds when sparse observations are unavailable", () =>
        {
            var project = new ProjectData();
            var photo = new Photo { Id = "reference", Width = 800, Height = 600, FocalPixels = 600 };
            var camera = new CameraPose { ImageId = photo.Id };
            var scene = new ReconstructionScene { Cameras = [camera, new CameraPose { ImageId = "other", Centre = [300, 400, 0] }] };
            var range = DepthRangeEstimator.Estimate(project, scene, photo, camera, default);
            Check(range.Source.Contains("fallback") && range.Far > 500, "Capture extent supplies the fallback instead of 100 metres");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            try
            {
                DepthRangeEstimator.Estimate(project, scene, photo, camera, cancellation.Token);
                throw new InvalidOperationException("Depth range inference must honour cancellation");
            }
            catch (OperationCanceledException)
            {
            }
        });

        tests.Run("Depth ranges reuse bounded storage across observed, visible and concurrent estimates", () =>
        {
            var project = new ProjectData();
            var photo = new Photo { Id = "reference", Width = 800, Height = 600, FocalPixels = 600 };
            var camera = new CameraPose { ImageId = photo.Id };
            var scene = new ReconstructionScene { Cameras = [camera, new CameraPose { ImageId = "other", Centre = [300, 400, 0] }] };
            scene.Cameras.Add(new CameraPose { ImageId = "remote", Centre = [-300, 400, 0] });
            for (var index = 0; index < 10000; index++)
            {
                scene.Points.Add(Point("other", 0, 0, 200, "remote"));
            }
            for (var index = 0; index < 9; index++)
            {
                scene.Points.Add(Point(photo.Id, 0, 0, 50));
            }
            var visible = DepthRangeEstimator.Estimate(project, scene, photo, camera, default);
            Check(visible.SampleCount == 10009 && visible.Near == 150 && visible.Far == 250,
                "Fewer than ten observed points must fall back to all visible samples and preserve trimming");
            scene.Points.Add(Point(photo.Id, 0, 0, 50));
            var observed = DepthRangeEstimator.Estimate(project, scene, photo, camera, default);
            Check(observed.SampleCount == 10 && observed.Near == 37.5 && observed.Far == 62.5,
                "Ten observed points must take precedence over unrelated visible depths");
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < 20; index++)
            {
                Check(DepthRangeEstimator.Estimate(project, scene, photo, camera, default) == observed, "Reused buffers retain no previous samples");
            }
            Check(GC.GetAllocatedBytesForCurrentThread() - allocated < 100000,
                "Warm estimates must not allocate point-sized arrays or growing tile lists");
            Parallel.For(0, 32, index =>
            {
                Check(DepthRangeEstimator.Estimate(project, scene, photo, camera, default) == observed,
                    "Concurrent images must not share mutable sample storage");
            });
            scene.Points.Clear();
            scene.Cameras.RemoveAt(2);
            var empty = DepthRangeEstimator.Estimate(project, scene, photo, camera, default);
            Check(empty.SampleCount == 0 && empty.Near == 10 && empty.Far == 2000,
                "Empty scenes must use camera bounds, without reading stale pooled entries");
        });

        tests.Run("Automatic depth reconstructs stereo texture beyond the old 100 metre cap", () =>
        {
            var store = stores.Open(Path.Combine(Path.GetTempPath(), "echo-depth-range-" + Guid.NewGuid().ToString("N")));
            store.Init();
            try
            {
                var project = new ProjectData { Settings = new Settings { DepthWidth = 160, DepthPlanes = 192, DepthNeighbours = 2 } };
                var scene = new ReconstructionScene();
                using var texture = new SKBitmap(160, 96);
                var random = new Random(33);
                for (var y = 0; y < texture.Height; y++)
                {
                    for (var x = 0; x < texture.Width; x++)
                    {
                        var value = (byte)random.Next(256);
                        texture.SetPixel(x, y, new SKColor(value, value, value));
                    }
                }
                foreach (var shift in new[] { 0, -8, 8 })
                {
                    var id = "view" + shift;
                    using var bitmap = new SKBitmap(texture.Width, texture.Height);
                    for (var y = 0; y < bitmap.Height; y++)
                    {
                        for (var x = 0; x < bitmap.Width; x++)
                        {
                            bitmap.SetPixel(x, y, texture.GetPixel(Math.Clamp(x + shift, 0, texture.Width - 1), y));
                        }
                    }
                    images.Save(bitmap, store.PathFor(id + ".png"), SKEncodedImageFormat.Png);
                    project.Images.Add(new Photo { Id = id, Source = id + ".png", Width = 160, Height = 96, FocalPixels = 120 });
                    scene.Cameras.Add(new CameraPose { ImageId = id, Centre = [shift * 1.25, 0, 0] });
                }
                for (var index = 0; index < 20; index++)
                {
                    scene.Points.Add(Point("view0", -30 + index * 3, 0, 150, "view-8"));
                }
                var map = estimator.Estimate(store, project, scene, scene.Cameras[0], default, 2);
                var accepted = map.Depth.Where(value => value > 0).ToArray();
                Check(accepted.Length > 9000, "Recover most visible texture at 150 metres");
                Check(accepted.Count(value => Math.Abs(value - 150) < 3) > accepted.Length * .99, "The extended search must recover the known plane, not merely fill pixels");
                project.Settings.DepthRangeAutomatic = false;
                var capped = estimator.Estimate(store, project, scene, scene.Cameras[0], default, 2);
                Check(capped.Depth.All(value => value <= 100), "Manual mode preserves an explicit range limit");
            }
            finally
            {
                Directory.Delete(store.Root, true);
            }
        });
    }

    private CloudPoint Point(string imageId, double x, double y, double z, string? otherImageId = null)
    {
        return new CloudPoint { ImageA = imageId, ImageB = otherImageId ?? (imageId == "other" ? "reference" : "other"), Position = [x, y, z], Error = .5 };
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
