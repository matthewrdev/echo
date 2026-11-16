using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.DenseCloud;
using Echo.Imaging;
using Echo.Geometry;
using Echo.Projects;
using SkiaSharp;

namespace Echo.Tests;

[Export]
public sealed class CloudProcessingTests
{
    private readonly ICloudBuilder builder;
    private readonly ICloudExecutionPlanner planner;
    private readonly IProjectStoreFactory stores;
    private readonly IRasterImageService images;

    [ImportingConstructor]
    public CloudProcessingTests(ICloudBuilder builder, ICloudExecutionPlanner planner, IProjectStoreFactory stores, IRasterImageService images)
    {
        this.builder = builder;
        this.planner = planner;
        this.stores = stores;
        this.images = images;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Dense cloud excludes box and cylinder samples before previews and output", () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "echo-crop-cloud-" + Guid.NewGuid().ToString("N"));
            var store = stores.Open(root);
            store.Init();
            try
            {
                var project = new ProjectData();
                var scene = new ReconstructionScene();
                var maps = MakePlane(store, project, scene);
                var original = builder.Build(store, project, scene, maps, default);
                foreach (var shape in Enum.GetValues<ExclusionShape>())
                {
                    var region = new ExclusionRegion { Shape = shape, CentreZ = 10, Width = 3, Height = 3, Depth = 3, Radius = 1.5 };
                    project.Settings.ExclusionRegions = [region];
                    var cloud = builder.Build(store, project, scene, maps, default);
                    Check(cloud.Samples.Count < original.Samples.Count, "Exclusion did not reduce the cloud");
                    Check(cloud.Samples.All(point => !ExclusionGeometry.Contains([region], new V3(point.X, point.Y, point.Z))),
                        "Excluded samples survived cloud validation");
                    Check(builder.Report(cloud).Preview.All(point => !ExclusionGeometry.Contains([region], new V3(point.Position))),
                        "Excluded points survived the preview");
                }
                project.Settings.ExclusionRegions.Clear();
                Check(builder.Build(store, project, scene, maps, default).Samples.SequenceEqual(original.Samples),
                    "Removing exclusions must restore the original cloud from saved depths");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        });
        tests.Run("Depth projection rejects points outside the image before integer conversion", () =>
        {
            var photo = new Photo { Width = 64, Height = 48, FocalPixels = 64 };
            var depths = new float[64 * 48];
            Array.Fill(depths, 10);
            var map = new DepthMap { Width = 64, Height = 48, Depth = depths };
            var view = new DepthView(photo, new CameraPose(), map);
            foreach (var point in new[]
            {
                new V3(1, 0, 1e-300), new V3(0, 1, 1e-300),
                new V3(-1, 0, 1e-300), new V3(0, -1, 1e-300),
                new V3(double.NaN, 0, 1), new V3(double.PositiveInfinity, 0, 1),
                new V3(0, 0, -1), new V3(0, 0, 0)
            })
            {
                Check(double.IsNaN(view.Difference(point)), "Unprojectable points have no supporting depth");
            }
            Check(view.Difference(new V3(0, 0, 10)) == 0, "Valid projections keep their depth difference");
        });
        tests.Run("Cloud support rejects depth-edge interpolation and inconsistent return projections", () =>
        {
            var photo = new Photo { Width = 64, Height = 48, FocalPixels = 64 };
            var depths = new float[64 * 48];
            Array.Fill(depths, 10);
            var map = new DepthMap { Width = 64, Height = 48, Depth = depths };
            var reference = new DepthView(photo, new CameraPose(), map);
            var point = new V3(0, 0, 10);
            Check(reference.TrySurfacePoint(point, .01, out var surface), "A continuous plane supplies support");
            Check(reference.ReprojectsTo(surface, 32, 24, .01), "Consistent support returns to the source pixel");
            depths[24 * 64 + 32] = 5;
            depths[24 * 64 + 33] = 15;
            var edge = new V3(10.0 / 128, 0, 10);
            Check(Math.Abs(reference.Difference(edge)) < 1e-8, "Legacy interpolation invents a depth between surfaces");
            Check(!reference.TrySurfacePoint(edge, .01, out _), "The invented surface is not accepted as support");
            Array.Fill(depths, 10.08f);
            var neighbour = new DepthView(photo, new CameraPose { Centre = [2, 0, 0] }, map);
            Check(neighbour.TrySurfacePoint(point, .01, out surface), "Depth tolerance alone accepts this sample");
            Check(!reference.ReprojectsTo(surface, 32, 24, .01), "Round-trip consistency rejects its pixel mismatch");
        });
        tests.Run("Cloud batch planning bounds pooled scratch for large images and worker counts", () =>
        {
            var settings = new Settings { CloudWorkerThreads = 256, CloudBatchRows = 128 };
            var plan = planner.Plan(settings, 8192, 8192);
            Check(plan.Workers > 1 && plan.RowsPerBatch >= 1, "Parallel work remains available");
            Check(plan.MaximumScratchBytes <= 64 * 1024 * 1024, "Worker scratch stays within its budget including pool rounding");
        });
        tests.Run("Cloud batches preserve ordered samples and publish progress only on the caller", () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "echo-cloud-" + Guid.NewGuid().ToString("N"));
            var store = stores.Open(root);
            store.Init();
            try
            {
                var project = new ProjectData();
                var scene = new ReconstructionScene();
                var maps = MakePlane(store, project, scene);
                project.Settings.CloudWorkerThreads = 1;
                var reference = builder.Build(store, project, scene, maps, default);
                var callerThread = Environment.CurrentManagedThreadId;
                foreach (var workers in new[] { 1, 2, 4 })
                {
                    foreach (var rows in new[] { 1, 7, 128 })
                    {
                        project.Settings.CloudWorkerThreads = workers;
                        project.Settings.CloudBatchRows = rows;
                        var completedRows = 0;
                        DenseCloudReport? lastPreview = null;
                        var cloud = builder.Build(store, project, scene, maps, default, update =>
                        {
                            Check(Environment.CurrentManagedThreadId == callerThread, "Progress is serialized on the caller");
                            Check(update.CompletedRows > completedRows, "Progress moves forward");
                            completedRows = update.CompletedRows;
                        }, preview =>
                        {
                            Check(Environment.CurrentManagedThreadId == callerThread, "Preview is serialized on the caller");
                            lastPreview = preview;
                        });
                        Check(reference.Samples.SequenceEqual(cloud.Samples), "Point order, coordinates, colours, views and confidence stay identical");
                        Check(reference.InputSamples == cloud.InputSamples, "Input counts stay identical");
                        Check(completedRows == 48 * maps.Count, "All rows reported");
                        Check(lastPreview?.RetainedSamples == cloud.Samples.Count, "Final preview contains the complete cloud count");
                    }
                }
                using var cancelled = new CancellationTokenSource();
                var callbacks = 0;
                try
                {
                    builder.Build(store, project, scene, maps, cancelled.Token, _ =>
                    {
                        callbacks++;
                        cancelled.Cancel();
                    });
                    throw new InvalidOperationException("Expected cancellation between batches");
                }
                catch (OperationCanceledException)
                {
                }
                Check(callbacks == 1, "No more progress callbacks after cancellation");
                var afterCancellation = builder.Build(store, project, scene, maps, default);
                Check(reference.Samples.SequenceEqual(afterCancellation.Samples), "Returned worker buffers remain safe to reuse");
                foreach (var camera in scene.Cameras)
                {
                    camera.Centre = [0, 0, 0];
                }
                try
                {
                    builder.Build(store, project, scene, maps, default);
                    throw new InvalidOperationException("Co-located cameras must not support a surface without parallax");
                }
                catch (EchoException error) when (error.ExitCode == 5)
                {
                }
            }
            finally
            {
                Directory.Delete(root, true);
            }
        });
    }

    private List<DepthMap> MakePlane(IProjectStore store, ProjectData project, ReconstructionScene scene)
    {
        var maps = new List<DepthMap>();
        for (var image = 0; image < 3; image++)
        {
            var id = "view" + image;
            var source = id + ".png";
            using var bitmap = new SKBitmap(64, 48);
            bitmap.Erase(new SKColor((byte)(image * 60), 80, 140));
            images.Save(bitmap, store.PathFor(source), SKEncodedImageFormat.Png);
            project.Images.Add(new Photo { Id = id, Source = source, Width = 64, Height = 48, FocalPixels = 64 });
            scene.Cameras.Add(new CameraPose { ImageId = id, Centre = [image * .25, 0, 0] });
            var depth = new float[64 * 48];
            for (var index = 0; index < depth.Length; index++)
            {
                depth[index] = index % 7 == image ? 0 : 10;
            }
            maps.Add(new DepthMap { ImageId = id, Width = 64, Height = 48, Depth = depth, Confidence = new float[depth.Length] });
        }
        return maps;
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
