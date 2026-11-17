using System.ComponentModel.Composition;
using Echo.Compute;
using Echo.Contracts;
using Echo.DenseCloud;
using Echo.Geometry;
using Echo.Imaging;
using Echo.Infrastructure;
using Echo.Pipeline;
using Echo.Projects;
using SkiaSharp;

namespace Echo.Tests;

[Export]
public sealed class CropPipelineTests
{
    private readonly IProjectStoreFactory stores;
    private readonly IRunContextFactory contexts;
    private readonly IFileOperations files;
    private readonly IJsonCodec json;
    private readonly ICloudBuilder clouds;
    private readonly IRasterImageService images;
    private readonly IComputeService compute;
    private readonly IPipelineStage cloudStage;
    private readonly IPipelineStage fusionStage;

    [ImportingConstructor]
    public CropPipelineTests(IProjectStoreFactory stores, IRunContextFactory contexts, IFileOperations files,
        IJsonCodec json, ICloudBuilder clouds, IRasterImageService images, IComputeService compute,
        [ImportMany] IEnumerable<IPipelineStage> stages)
    {
        this.stores = stores;
        this.contexts = contexts;
        this.files = files;
        this.json = json;
        this.clouds = clouds;
        this.images = images;
        this.compute = compute;
        cloudStage = stages.Single(stage => stage.Name == "cloud");
        fusionStage = stages.Single(stage => stage.Name == "fusion");
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Crop edits reuse the full cloud and rebuild fusion", () => WithProject(store =>
        {
            var original = Process(store, fusion: true);
            var cloudPath = original.Artifacts["cloud"];
            var cloudHash = files.HashFile(store.PathFor(cloudPath + "/cloud.ply"));
            var region = new ExclusionRegion { Mode = CropRegionMode.Include, CentreZ = 10, Width = 2, Height = 2, Depth = 1 };
            store.Edit(project => project.Settings.ExclusionRegions = [region]);
            var included = Process(store, fusion: true);
            Check(included.Artifacts["cloud"] == cloudPath, "Include edits rebuilt the point cloud");
            Check(included.Artifacts["fusion"] != original.Artifacts["fusion"], "Include edits reused stale fusion");
            var volume = json.Read<FusedVolume>(store.PathFor(included.Artifacts["fusion"] + "/volume.json"));
            var crop = new RegionCropFilter([region]);
            Check(volume.Samples.Count > 0 && volume.Samples.All(sample => !crop.IsExcluded(
                new V3(sample.X * volume.VoxelSize, sample.Y * volume.VoxelSize, sample.Z * volume.VoxelSize))),
                "Fusion ignored the downstream crop");
            store.Edit(project => project.Settings.ExclusionRegions[0].Mode = CropRegionMode.Exclude);
            var excluded = Process(store, fusion: true);
            Check(excluded.Artifacts["cloud"] == cloudPath && excluded.Artifacts["fusion"] != included.Artifacts["fusion"],
                "Changing crop mode must affect fusion only");
            store.Edit(project => project.Settings.ExclusionRegions.Clear());
            var restored = Process(store, fusion: true);
            Check(restored.Artifacts["cloud"] == cloudPath && restored.Artifacts["fusion"] == original.Artifacts["fusion"],
                "Removing the crop must restore the original cached outputs");
            Check(files.HashFile(store.PathFor(cloudPath + "/cloud.ply")) == cloudHash, "Saved full cloud was modified");
            Check(store.GetRun(original.Id).Snapshot.Settings.ExclusionRegions.Count == 0, "Original snapshot was changed");
            store.Edit(project => project.Settings.CloudDepthTolerance *= 2);
            Check(Process(store).Artifacts["cloud"] != cloudPath, "Cloud validation settings must still invalidate cloud generation");
        }));
        foreach (var version in new[] { 4, 5, 6, 7 })
        {
            tests.Run($"Crop edits reuse an existing complete version {version} cloud", () => WithProject(store =>
            {
                var legacy = SaveLegacy(store, version, cropped: false);
                store.Edit(project => project.Settings.ExclusionRegions = [new ExclusionRegion { Mode = CropRegionMode.Include }]);
                var run = Process(store);
                Check(run.Artifacts["cloud"] == legacy, "A compatible full cloud was needlessly rebuilt");
                var events = File.ReadAllText(store.PathFor($"runs/{run.Id}/events.ndjson"));
                Check(events.Contains("artifactReused") && !events.Contains("stageStarted"), "Cache reuse performed cloud processing");
            }));
        }
        tests.Run("Previously cropped checkpoints cannot become full-cloud inputs", () => WithProject(store =>
        {
            var legacy = SaveLegacy(store, 7, cropped: true);
            var run = Process(store);
            Check(run.Artifacts["cloud"] != legacy, "A cropped legacy checkpoint was reused as the complete cloud");
            var report = json.Read<DenseCloudReport>(store.PathFor(run.Artifacts["cloud"] + "/cloud.json"));
            Check(report.RetainedSamples > 1000, "The complete cloud was not regenerated from original depth maps");
        }));
        tests.Run("Compatible cloud reuse honours reset revisions and checkpoint integrity", () => WithProject(store =>
        {
            var legacy = SaveLegacy(store, 7, cropped: false);
            store.Edit(project => project.StageRevisions["cloud"] = "reset-one");
            var reset = SaveLegacy(store, 7, cropped: false);
            Check(reset != legacy && Process(store).Artifacts["cloud"] == reset, "Compatibility bypassed the requested reset revision");
            File.AppendAllText(store.PathFor(reset + "/cloud.ply"), "corrupt");
            try
            {
                Process(store);
                throw new InvalidOperationException("Expected corrupt compatible cloud to be rejected");
            }
            catch (EchoException error) when (error.Message.Contains("Corrupt checkpoint"))
            {
            }
        }));
        var savedProject = Environment.GetEnvironmentVariable("ECHO_TEST_CROP_PROJECT");
        var savedRun = Environment.GetEnvironmentVariable("ECHO_TEST_CROP_RUN");
        if (savedProject != null && savedRun != null)
        {
            tests.Run("Saved full-cloud artifact key remains reusable after downstream cropping change", () => CheckSavedCloud(savedProject, savedRun));
        }
    }

    private void CheckSavedCloud(string root, string runID)
    {
        var store = stores.Open(root);
        var run = store.GetRun(runID);
        var settings = run.Snapshot.Settings;
        var units = json.Read<List<string>>(store.PathFor(run.Artifacts["depth"] + "/units.json"));
        var implementation = settings.ComputeBackend == ComputeBackend.Cpu ? "cpu" : compute.GetImplementation(ComputeOperation.Cloud);
        var manifest = json.Read<Artifact>(store.PathFor(run.Artifacts["cloud"] + "/manifest.json"));
        foreach (var version in new[] { 4, 5, 6, 7, 8 })
        {
            object inputs = new DenseCloudInputs(units, run.Artifacts["alignment"], settings, implementation, version);
            if (run.Snapshot.StageRevisions.TryGetValue("cloud", out var resetRevision))
            {
                inputs = new
                {
                    resetRevision,
                    inputs
                };
            }
            var key = files.Hash(new
            {
                storageVersion = 3,
                version = manifest.AlgorithmVersion,
                stage = "cloud",
                inputs
            });
            if (key == manifest.Key)
            {
                return;
            }
        }
        throw new InvalidOperationException("Saved cloud does not match a compatible complete-cloud key");
    }

    private void WithProject(Action<IProjectStore> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "echo-crop-pipeline-" + Guid.NewGuid().ToString("N"));
        var store = stores.Open(root);
        store.Init();
        try
        {
            var fixture = new FusionFixture();
            foreach (var photo in fixture.Project.Images)
            {
                photo.Source = photo.Id + ".png";
                using var bitmap = new SKBitmap(64, 64);
                bitmap.Erase(SKColors.Gray);
                images.Save(bitmap, store.PathFor(photo.Source), SKEncodedImageFormat.Png);
            }
            fixture.Project.Settings.ComputeBackend = ComputeBackend.Cpu;
            fixture.Project.Settings.CloudMinimumSupport = 1;
            fixture.Project.Settings.FusionVoxelSize = .25;
            store.Edit(project =>
            {
                project.Images = fixture.Project.Images;
                project.Settings = fixture.Project.Settings;
            });
            files.AtomicData(store.PathFor("artifacts/alignment/test/scene.json"), fixture.Scene);
            var units = new List<string>();
            foreach (var map in fixture.Maps)
            {
                var unit = "artifacts/depth-image/" + map.ImageId;
                units.Add(unit);
                files.AtomicData(store.PathFor(unit + "/depth.json"), map);
            }
            files.AtomicJson(store.PathFor("artifacts/depth/test/units.json"), units);
            action(store);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private RunData NewRun(IProjectStore store) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Target = "fusion",
        Snapshot = store.Load(),
        Artifacts = new Dictionary<string, string> { ["alignment"] = "artifacts/alignment/test", ["depth"] = "artifacts/depth/test" }
    };

    private RunData Process(IProjectStore store, bool fusion = false)
    {
        var run = NewRun(store);
        using var context = contexts.Create(store, run, default, null);
        context.Start();
        cloudStage.Execute(context);
        if (fusion)
        {
            fusionStage.Execute(context);
        }
        context.Complete();
        return run;
    }

    private string SaveLegacy(IProjectStore store, int version, bool cropped)
    {
        var run = NewRun(store);
        using var context = contexts.Create(store, run, default, null);
        context.Start();
        var settings = run.Snapshot.Settings;
        var depthUnits = context.DepthUnits();
        var alignment = context.Artifact("alignment");
        // Independent historical payloads verify exact serialized key compatibility.
        object inputs = version == 4 ? new
        {
            depthUnits,
            alignment,
            cloudVersion = version,
            settings.ComputeBackend,
            computeImplementation = "cpu",
            settings.CloudNeighbours,
            settings.CloudMinimumSupport,
            settings.CloudDepthTolerance,
            settings.CloudReprojectionTolerancePixels,
            settings.CloudMinimumParallaxDegrees
        } : new
        {
            depthUnits,
            alignment,
            cloudVersion = version,
            ExclusionRegions = cropped ? new[] { new ExclusionRegion() } : Array.Empty<ExclusionRegion>(),
            settings.ComputeBackend,
            computeImplementation = "cpu",
            settings.CloudNeighbours,
            settings.CloudMinimumSupport,
            settings.CloudDepthTolerance,
            settings.CloudReprojectionTolerancePixels,
            settings.CloudMinimumParallaxDegrees
        };
        var artifact = context.Build("cloud", inputs, directory =>
        {
            var cloud = new FusionFixture().Cloud;
            clouds.Write(Path.Combine(directory, "cloud.ply"), cloud);
            files.AtomicJson(Path.Combine(directory, "cloud.json"), clouds.Report(cloud));
        });
        context.Complete();
        return artifact;
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
