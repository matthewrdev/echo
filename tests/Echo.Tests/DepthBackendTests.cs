using System.ComponentModel.Composition;
using System.Text.Json;
using Echo.Contracts;
using Echo.Depth;
using Echo.Imaging;
using Echo.Projects;
using Echo.Pipeline;
using Echo.Infrastructure;
using SkiaSharp;

namespace Echo.Tests;

[Export]
public sealed class DepthBackendTests
{
    private readonly IRasterImageService images;
    private readonly IProjectStoreFactory stores;
    private readonly IDepthEstimator composedEstimator;
    private readonly IRunContextFactory contexts;
    private readonly IFileOperations files;
    private readonly IJsonCodec json;
    private readonly IDepthExecutionPlanner planner;

    [ImportingConstructor]
    public DepthBackendTests(IRasterImageService images, IProjectStoreFactory stores, IDepthEstimator composedEstimator,
        IRunContextFactory contexts, IFileOperations files, IJsonCodec json, IDepthExecutionPlanner planner)
    {
        this.images = images;
        this.stores = stores;
        this.composedEstimator = composedEstimator;
        this.contexts = contexts;
        this.files = files;
        this.json = json;
        this.planner = planner;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Replaceable depth accelerator preserves CPU output on differences, unavailability and command failure", () =>
            WithScene((store, project, scene) =>
            {
                var cpu = new DepthEstimator(images, []);
                var reference = cpu.Estimate(store, project, scene, scene.Cameras[0], default, 2);
                Check(reference.Depth.Count(value => value > 0) > 1000, "Fixture needs accepted surface samples");
                project.Settings.DepthBackend = "metal-validated";
                foreach (var mode in new[] { "incorrect", "unavailable", "failure", "invalid-count" })
                {
                    var backend = new TestDepthCostAccelerator(mode);
                    var estimator = new DepthEstimator(images, [backend]);
                    var execution = new List<ProcessingExecution>();
                    var actual = estimator.Estimate(store, project, scene, scene.Cameras[0], default, 3, execution: execution.Add);
                    Check(reference.Depth.AsSpan().SequenceEqual(actual.Depth), mode + " depth changed");
                    Check(reference.Confidence.AsSpan().SequenceEqual(actual.Confidence), mode + " confidence changed");
                    Check(execution.Count == 2, "Record every pyramid level");
                    Check(execution.All(item => item.RequestedBackend == "metal-validated" && item.FallbackReason != null), "Record requests and corrections/fallbacks");
                    Check(backend.Sessions.All(session => session.Disposed), "Release all native sessions");
                    if (mode == "incorrect")
                    {
                        Check(execution.Sum(item => item.CorrectedSearches) > 1000, "Deliberately incorrect GPU decisions must be corrected");
                        Check(execution.All(item => item.ActualBackend == "metal+cpu"), "Record actual GPU participation");
                    }
                    else
                    {
                        Check(execution.All(item => item.ActualBackend == "cpu" && item.AcceleratedSearches == 0), "Failed attempts must not be labelled Metal output");
                    }
                }
                var cancelled = new TestDepthCostAccelerator("cancel");
                try
                {
                    new DepthEstimator(images, [cancelled]).Estimate(store, project, scene, scene.Cameras[0], default, 1);
                    throw new InvalidOperationException("Cancellation must propagate");
                }
                catch (Exception error) when (error is OperationCanceledException || error is AggregateException aggregate && aggregate.InnerExceptions.All(item => item is OperationCanceledException))
                {
                }
                Check(cancelled.Sessions.All(session => session.Disposed), "Cancellation must dispose native sessions");
            }));

        tests.Run("Automatic prefers Metal without CPU duplication and explicit CPU bypasses the accelerator", () =>
            WithScene((store, project, scene) =>
            {
                Check(new Settings().DepthBackend == "auto", "New projects prefer Metal");
                Check(JsonSerializer.Deserialize<Settings>("{}", json.Options)!.DepthBackend == "auto", "Missing backend defaults to automatic");
                Check(JsonSerializer.Deserialize<Settings>("{\"depthBackend\":\"cpu\"}", json.Options)!.DepthBackend == "cpu", "Preserve explicit CPU selection");
                project.Settings.DepthBackend = "auto";
                project.Settings.Validate();
                var backend = new TestDepthCostAccelerator("reject");
                var estimator = new DepthEstimator(images, [backend]);
                var execution = new List<ProcessingExecution>();
                var map = estimator.Estimate(store, project, scene, scene.Cameras[0], default, 2, execution: execution.Add);
                Check(map.Depth.All(value => value == 0), "Fast mode must use the supplied GPU costs");
                Check(execution.All(item => item.ActualBackend == "metal" && item.AcceleratedSearches > 0), "Automatic selects available Metal");
                Check(execution.All(item => item.CpuSearches == 0 && item.ValidatedSearches == 0 && item.CorrectedSearches == 0), "Fast mode must not repeat CPU searches or claim reference validation");
                Check(backend.Sessions.All(session => session.Disposed), "Dispose fast sessions");
                project.Settings.DepthBackend = "cpu";
                var ignored = new TestDepthCostAccelerator("cancel");
                map = new DepthEstimator(images, [ignored]).Estimate(store, project, scene, scene.Cameras[0], default, 2);
                Check(ignored.Sessions.Count == 0 && map.Depth.Any(value => value > 0), "Explicit CPU never opens Metal");
            }));

        tests.Run("Fast Metal falls back on device, command and malformed cost failures and records mixed execution", () =>
            WithScene((store, project, scene) =>
            {
                var reference = new DepthEstimator(images, []).Estimate(store, project, scene, scene.Cameras[0], default, 1);
                foreach (var requested in new[] { "auto", "metal" })
                {
                    project.Settings.DepthBackend = requested;
                    project.Settings.Validate();
                    foreach (var mode in new[] { "unavailable", "failure", "invalid-count", "nan", "negative", "too-large" })
                    {
                        var backend = new TestDepthCostAccelerator(mode);
                        var execution = new List<ProcessingExecution>();
                        var actual = new DepthEstimator(images, [backend]).Estimate(store, project, scene, scene.Cameras[0], default, 1, execution: execution.Add);
                        Check(reference.Depth.AsSpan().SequenceEqual(actual.Depth) && reference.Confidence.AsSpan().SequenceEqual(actual.Confidence), mode + " fallback must preserve CPU output");
                        Check(execution.All(item => item.ActualBackend == "cpu" && item.CpuSearches > 0 && item.FallbackReason != null), mode + " must record the actual fallback");
                        Check(backend.Sessions.All(session => session.Disposed), "Release failed sessions");
                    }
                }
                var partial = new TestDepthCostAccelerator("partial-failure");
                var mixed = new List<ProcessingExecution>();
                new DepthEstimator(images, [partial]).Estimate(store, project, scene, scene.Cameras[0], default, 1, execution: mixed.Add);
                Check(mixed.All(item => item.ActualBackend == "metal+cpu" && item.AcceleratedSearches > 0 && item.CpuSearches > 0), "Record GPU results followed by CPU fallback");
                Check(mixed.All(item => item.ValidatedSearches == 0 && item.CorrectedSearches == 0 && item.FallbackReason != null), "Fallback is not CPU validation");
                var cancelled = new TestDepthCostAccelerator("cancel");
                try
                {
                    new DepthEstimator(images, [cancelled]).Estimate(store, project, scene, scene.Cameras[0], default, 1);
                    throw new InvalidOperationException("Fast mode must propagate cancellation");
                }
                catch (Exception error) when (error is OperationCanceledException || error is AggregateException aggregate && aggregate.InnerExceptions.All(item => item is OperationCanceledException))
                {
                }
                Check(cancelled.Sessions.All(session => session.Disposed), "Dispose cancelled fast sessions");
            }));

        if (Environment.GetEnvironmentVariable("ECHO_TEST_METAL") == "1")
        {
            tests.Run("Native Metal depth module executes on GPU and produces bit-identical complete pyramid maps", () =>
                WithScene((store, project, scene) =>
                {
                    project.Settings.DepthBackend = "cpu";
                    var reference = composedEstimator.Estimate(store, project, scene, scene.Cameras[0], default, 2);
                    project.Settings.DepthBackend = "metal-validated";
                    project.Settings.DepthWorkerThreads = 3;
                    var run = new RunData { Id = Guid.NewGuid().ToString("N"), Snapshot = project, Target = "depth" };
                    var context = contexts.Create(store, run, default, null);
                    context.Start();
                    var alignment = context.Build("alignment", new
                    {
                        fixture = "backend"
                    }, directory => files.AtomicJson(Path.Combine(directory, "scene.json"), scene));
                    context.Publish("alignment", alignment);
                    new DepthStage(files, json, composedEstimator, planner).Execute(context);
                    context.Complete();
                    var unit = context.DepthUnits()[0];
                    var actual = json.Read<DepthMap>(store.PathFor(unit + "/depth.json"));
                    var metadata = json.Read<ProcessingMetadata>(store.PathFor(unit + "/processing.json"));
                    var execution = metadata.Executions;
                    Check(execution.All(item => item.AcceleratedSearches > 0), "GPU must execute at every level; fallback-only is not a GPU integration pass");
                    Check(execution.Count == 2 && metadata.MemoryBudgetBytes > 0, "Persist all levels and resolved scheduling resources");
                    var saved = json.Read<RunProcessingMetadata>(store.PathFor($"runs/{run.Id}/processing.json"));
                    Check(saved.Artifacts[unit].MetadataPath == unit + "/processing.json", "Run must index the actual Metal provenance");
                    Check(reference.Depth.AsSpan().SequenceEqual(actual.Depth), "Metal depth must match CPU exactly");
                    Check(reference.Confidence.AsSpan().SequenceEqual(actual.Confidence), "Metal confidence must match CPU exactly");
                    Console.WriteLine($"Metal device: {execution[0].Device}; validated {execution.Sum(item => item.AcceleratedSearches)} searches, corrected {execution.Sum(item => item.CorrectedSearches)}");
                    project.Settings.DepthBackend = "auto";
                    var accelerated = RunDepthStage(store, project, alignment);
                    var fastUnit = accelerated.DepthUnits()[0];
                    Check(fastUnit != unit, "Fast Metal cannot reuse CPU/validated depth checkpoints");
                    var fastMetadata = json.Read<ProcessingMetadata>(store.PathFor(fastUnit + "/processing.json"));
                    Check(fastMetadata.AlgorithmVersion == "echo-0.1.0-pyramid8-metal1", "Version the direct Metal numerical contract");
                    Check(fastMetadata.Executions.All(item => item.ActualBackend == "metal" && item.CpuSearches == 0), "Persist actual direct GPU execution without duplicate CPU work");
                    var fastRunMetadata = json.Read<RunProcessingMetadata>(store.PathFor($"runs/{accelerated.Run.Id}/processing.json"));
                    Check(fastRunMetadata.Artifacts[fastUnit].MetadataPath == fastUnit + "/processing.json", "Run indexes fast Metal provenance");
                    var originalMetadata = File.ReadAllText(store.PathFor(fastUnit + "/processing.json"));
                    project.Settings.DepthBackend = "metal";
                    var reusedFast = RunDepthStage(store, project, alignment);
                    Check(reusedFast.DepthUnits()[0] == fastUnit, "Automatic and explicit Metal share compatible maps");
                    Check(File.ReadAllText(store.PathFor(fastUnit + "/processing.json")) == originalMetadata, "Reuse preserves original device and requested backend");
                    project.Settings.DepthBackend = "cpu";
                    Check(RunDepthStage(store, project, alignment).DepthUnits()[0] == unit, "CPU can still reuse the original validated map");
                }));
            tests.Run("Fast Metal preserves geometry across independent textures, low contrast and translated world origins", () =>
            {
                WithScene(CheckFastScene, 83);
                WithScene(CheckFastScene, 197, true);
                WithScene(CheckFastScene, 83, false, 1000000);
            });

        }
    }

    private RunContext RunDepthStage(IProjectStore store, ProjectData project, string alignment)
    {
        var run = new RunData { Id = Guid.NewGuid().ToString("N"), Snapshot = json.Clone(project), Target = "depth" };
        var context = contexts.Create(store, run, default, null);
        context.Start();
        context.Publish("alignment", alignment);
        new DepthStage(files, json, composedEstimator, planner).Execute(context);
        context.Complete();
        return context;
    }

    private void CheckFastScene(IProjectStore store, ProjectData project, ReconstructionScene scene)
    {
        project.Settings.DepthBackend = "cpu";
        var reference = composedEstimator.Estimate(store, project, scene, scene.Cameras[0], default, 2);
        project.Settings.DepthBackend = "auto";
        var execution = new List<ProcessingExecution>();
        var actual = composedEstimator.Estimate(store, project, scene, scene.Cameras[0], default, 3, execution: execution.Add);
        Check(execution.All(item => item.ActualBackend == "metal" && item.CpuSearches == 0), "Must exercise direct Metal at every level");
        var repeated = composedEstimator.Estimate(store, project, scene, scene.Cameras[0], default, 1);
        Check(actual.Depth.AsSpan().SequenceEqual(repeated.Depth) && actual.Confidence.AsSpan().SequenceEqual(repeated.Confidence), "Metal must be deterministic across worker counts");
        var errors = new List<double>();
        var validityChanges = 0;
        var largeDifferences = 0;
        for (var index = 0; index < actual.Depth.Length; index++)
        {
            var expected = reference.Depth[index];
            var value = actual.Depth[index];
            Check(float.IsFinite(value) && float.IsFinite(actual.Confidence[index]), "Finite maps");
            validityChanges += (expected > 0) != (value > 0) ? 1 : 0;
            if (expected > 0 && value > 0)
            {
                var error = Math.Abs((double)value - expected) / expected;
                errors.Add(error);
                largeDifferences += error > .01 ? 1 : 0;
            }
        }
        errors.Sort();
        Check(errors.Count > 1000, "Fixture must reconstruct a supported surface");
        var p99 = errors[(int)((errors.Count - 1) * .99)];
        Console.WriteLine($"Fast Metal parity: {errors.Count} common samples, {validityChanges} mask changes, p99={p99:G6}, >1%={largeDifferences}");
        Check(validityChanges <= reference.Depth.Length * .001, "At most 0.1% of pixels may change validity");
        Check(p99 <= .001, "99% of shared depths must agree within 0.1%");
        Check(largeDifferences <= errors.Count * .001, "At most 0.1% of shared depths may differ by more than 1%");
        var wrongGeometry = actual.Depth.Count(value => value > 0 && Math.Abs(value - 10) > .15);
        Check(wrongGeometry <= errors.Count * .01, "Preserve the independently known plane at depth 10");
    }

    private void WithScene(Action<IProjectStore, ProjectData, ReconstructionScene> action, int seed = 61, bool lowContrast = false, double worldOffset = 0)
    {
        var store = stores.Open(Path.Combine(Path.GetTempPath(), "echo-backend-" + Guid.NewGuid().ToString("N")));
        store.Init();
        try
        {
            using var texture = new SKBitmap(192, 128);
            var random = new Random(seed);
            for (var y = 0; y < texture.Height; y++)
            {
                for (var x = 0; x < texture.Width; x++)
                {
                    var shade = (byte)(lowContrast ? random.Next(115, 141) : random.Next(30, 225));
                    texture.SetPixel(x, y, new SKColor(shade, shade, shade));
                }
            }
            var project = new ProjectData
            {
                Settings = new Settings { DepthWidth = 192, DepthPlanes = 49, DepthNeighbours = 2, DepthRangeAutomatic = false, DepthNear = 8, DepthFar = 12 }
            };
            var scene = new ReconstructionScene();
            foreach (var shift in new[] { 0, -6, 6 })
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
                project.Images.Add(new Photo { Id = id, Source = id + ".png", Width = 192, Height = 128, FocalPixels = 150 });
                scene.Cameras.Add(new CameraPose { ImageId = id, Centre = [worldOffset + shift / 15.0, worldOffset, worldOffset] });
            }
            action(store, project, scene);
        }
        finally
        {
            Directory.Delete(store.Root, true);
        }
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
