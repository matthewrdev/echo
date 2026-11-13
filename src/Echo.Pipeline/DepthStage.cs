using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Depth;
using Echo.Infrastructure;

namespace Echo.Pipeline;

[Export(typeof(IPipelineStage)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class DepthStage : IPipelineStage
{
    private readonly IFileOperations fileOperations;
    private readonly IJsonCodec jsonCodec;
    private readonly IDepthEstimator depthEstimator;
    private readonly IDepthExecutionPlanner executionPlanner;

    [ImportingConstructor]
    public DepthStage(IFileOperations fileOperations, IJsonCodec jsonCodec, IDepthEstimator depthEstimator, IDepthExecutionPlanner executionPlanner)
    {
        this.fileOperations = fileOperations;
        this.jsonCodec = jsonCodec;
        this.depthEstimator = depthEstimator;
        this.executionPlanner = executionPlanner;
    }

    public string Name => "depth";
    public int Order => 3;

    public void Execute(RunContext context)
    {
        var project = context.Run.Snapshot;
        var store = context.Store;
        var cancellation = context.Cancellation;
        var alignment = context.Artifact("alignment");
        var scene = jsonCodec.Read<ReconstructionScene>(store.PathFor(alignment + "/scene.json"));
        var depthUnits = new string[scene.Cameras.Count];
        var previewUnits = context.Run.Artifacts.ContainsKey("depthPreview")
            ? context.ReadArtifact<Dictionary<string, string>>("depthPreview", "units.json")
            : new Dictionary<string, string>();
        var completed = 0;
        var progressGate = new object();
        var plan = executionPlanner.Plan(project, scene.Cameras.Count);
        context.ReportProgress(Name, $"Depth: {plan.ConcurrentImages} images concurrently, {plan.WorkersPerImage} CPU workers per image; estimated {plan.EstimatedBytesPerImage / 1024 / 1024:N0} MB per job.", 0, scene.Cameras.Count);
        var options = new ParallelOptions { CancellationToken = cancellation, MaxDegreeOfParallelism = plan.ConcurrentImages };
        Parallel.For(0, scene.Cameras.Count, options, index =>
        {
            var camera = scene.Cameras[index];
            var photo = project.Images.Single(image => image.Id == camera.ImageId);
            var inputs = new
            {
                alignment,
                camera.ImageId,
                project.Settings.DepthWidth,
                project.Settings.DepthPlanes,
                project.Settings.DepthRefinementSamples,
                project.Settings.DepthNeighbours,
                project.Settings.DepthNear,
                project.Settings.DepthFar
            };
            DepthMap? detected = null;
            var depth = context.Build("depth-image", inputs, directory =>
            {
                var map = depthEstimator.Estimate(store, project, scene, camera, cancellation, plan.WorkersPerImage, message =>
                {
                    lock (progressGate)
                    {
                        context.ReportProgress(Name, $"Image {index + 1}/{scene.Cameras.Count} · {photo.Name} · {message}", completed, scene.Cameras.Count);
                    }
                });
                detected = map;
                cancellation.ThrowIfCancellationRequested();
                lock (progressGate)
                {
                    context.ReportProgress(Name, $"Image {index + 1}/{scene.Cameras.Count} · {photo.Name} · saving depth and confidence maps", completed, scene.Cameras.Count);
                }
                fileOperations.AtomicJson(Path.Combine(directory, "depth.json"), map);
            });
            depthUnits[index] = depth;
            var reused = detected == null;
            var savedMap = detected ?? jsonCodec.Read<DepthMap>(store.PathFor(depth + "/depth.json"));
            var validSamples = savedMap.Depth.Count(value => float.IsFinite(value) && value > 0);
            var sampleCount = savedMap.Depth.Length;
            lock (progressGate)
            {
                completed++;
                if (!previewUnits.TryGetValue(camera.ImageId, out var previous) || previous != depth)
                {
                    previewUnits[camera.ImageId] = depth;
                    PublishPreview(context, previewUnits);
                }
                var action = reused ? "Reused" : "Found";
                context.Report("imageCheckpoint", new
                {
                    stage = "depth",
                    imageId = camera.ImageId,
                    imageName = photo.Name,
                    validSamples,
                    sampleCount,
                    reused,
                    message = $"{photo.Name} · {action} {validSamples:N0}/{sampleCount:N0} valid depth samples ({100.0 * validSamples / sampleCount:F2}%) · {completed:N0}/{scene.Cameras.Count:N0} images ready",
                    completed,
                    total = scene.Cameras.Count
                });
            }
        });

        var artifact = context.Build("depth", depthUnits, directory =>
        {
            fileOperations.AtomicJson(Path.Combine(directory, "units.json"), depthUnits);
        });
        context.Publish("depth", artifact);
    }

    private void PublishPreview(RunContext context, Dictionary<string, string> units)
    {
        // Concurrent images may finish in any order. Keep explicit image IDs in
        // the preview while preserving camera order in the final depth index.
        var artifact = context.Build("depth-preview", units, directory =>
        {
            fileOperations.AtomicJson(Path.Combine(directory, "units.json"), units);
        });
        context.Publish("depthPreview", artifact);
    }
}
