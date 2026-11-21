using Echo.Compute;
using Echo.Contracts;
using Echo.Depth;
using Echo.Infrastructure;

namespace Echo.Pipeline;

public sealed class DepthStage : IPipelineStage
{
    private readonly IFileOperations fileOperations;
    private readonly IJsonCodec jsonCodec;
    private readonly IDepthEstimator depthEstimator;
    private readonly IDepthExecutionPlanner executionPlanner;
    private readonly ISceneSemanticArtifacts semanticArtifacts;
    private readonly ICliffRecoveryArtifacts cliffRecovery;

    public DepthStage(IFileOperations fileOperations, IJsonCodec jsonCodec, IDepthEstimator depthEstimator, IDepthExecutionPlanner executionPlanner, ISceneSemanticArtifacts semanticArtifacts, ICliffRecoveryArtifacts cliffRecovery)
    {
        this.fileOperations = fileOperations;
        this.jsonCodec = jsonCodec;
        this.depthEstimator = depthEstimator;
        this.executionPlanner = executionPlanner;
        this.semanticArtifacts = semanticArtifacts;
        this.cliffRecovery = cliffRecovery;
    }

    public string Name => "depth";
    public int Order => 3;

    public void Execute(RunContext context)
    {
        var project = context.Run.Snapshot;
        var store = context.Store;
        var cancellation = context.Cancellation;
        var alignment = context.Artifact("alignment");
        var scene = context.Scene();
        var subjectDistance = SubjectDistancePlanner.Estimate(project, scene, cancellation);
        var subjectDescription = subjectDistance.MaximumDistance.HasValue
            ? $"Subject distance limit: {subjectDistance.MaximumDistance:F2}; {subjectDistance.Source}; capture extent {subjectDistance.CaptureExtent:F2}, foreground depth {subjectDistance.ForegroundDepth:F2}, {subjectDistance.EvidenceCameras} evidence cameras (metres when georeferenced)."
            : $"Subject distance: {subjectDistance.Source}.";
        context.ReportProgress(Name, subjectDescription);
        var plan = executionPlanner.Plan(project, scene.Cameras.Count, context.WorkingMemoryBytes, context.Resources.DepthMemoryBudgetMb);
        var needsSemantics = project.Settings.DepthSuppressSky || project.Settings.OptimiseVegetation || project.Settings.RecoverCliffDepth;
        var steps = new List<string>();
        if (needsSemantics)
        {
            steps.Add("Classify sky, rock and vegetation");
        }

        steps.Add("Estimate stereo depth");
        if (project.Settings.RecoverCliffDepth)
        {
            steps.Add("Recover missing cliff detail");
        }

        steps.Add("Save depth result");
        var stepNames = steps.ToArray();
        var stepIndex = 0;
        if (needsSemantics)
        {
            context.BeginStep(Name, stepNames, stepIndex++, context.Images.Count);
        }

        var semantics = needsSemantics
            ? semanticArtifacts.Build(context) : new Dictionary<string, SemanticMap>();
        context.BeginStep(Name, stepNames, stepIndex++, scene.Cameras.Count);
        var depthUnits = new string[scene.Cameras.Count];
        var previewUnits = context.Run.Artifacts.ContainsKey("depthPreview")
            ? context.ReadArtifact<Dictionary<string, string>>("depthPreview", "units.json")
            : new Dictionary<string, string>();
        var resumingPartialPreview = previewUnits.Count > 0 && !context.Run.Artifacts.ContainsKey("depth");
        var completed = 0;
        var progressGate = new object();
        // Version adaptive overlap selection, fine-image prefiltering and sky filtering. Direct Metal
        // stays separate; CPU and fully validated Metal share a numerical contract.
        var algorithmVersion = project.Settings.DepthBackend is "auto" or "metal" ? "echo-depth-pyramid12-metal-overlap1-sky1-subject2" : "echo-depth-pyramid12-cpu-overlap1-sky1-subject2";
        var budgetMode = context.Resources.DepthMemoryBudgetMb == 0 ? "automatic" : "manual";
        context.ReportProgress(Name, $"Depth: {plan.ConcurrentImages} images concurrently, {plan.WorkersPerImage} CPU workers per image; estimated {plan.EstimatedBytesPerImage / 1024 / 1024:N0} MB per job; {budgetMode} memory budget {plan.MemoryBudgetBytes / 1024 / 1024:N0} MB.", 0, scene.Cameras.Count);
        var options = new ParallelOptions { CancellationToken = cancellation, MaxDegreeOfParallelism = plan.ConcurrentImages };
        Parallel.For(0, scene.Cameras.Count, options, index =>
        {
            var camera = scene.Cameras[index];
            var photo = project.Images.Single(image => image.Id == camera.ImageId);
            var inputs = new
            {
                alignment,
                camera.ImageId,
                project.Settings.ComputeBackend,
                computeImplementation = context.ComputeImplementation(ComputeOperation.Depth),
                project.Settings.DepthSuppressSky,
                skySemantics = project.Settings.DepthSuppressSky ? context.Artifact("semantics") : null,
                project.Settings.DepthWidth,
                project.Settings.DepthPlanes,
                project.Settings.DepthRefinementSamples,
                project.Settings.DepthNeighbours,
                project.Settings.DepthRangeAutomatic,
                project.Settings.DepthNear,
                project.Settings.DepthFar,
                subjectDistance
            };
            DepthMap? detected = null;
            var processing = new ProcessingMetadata
            {
                MemoryBudgetBytes = plan.MemoryBudgetBytes,
                ConcurrentImages = plan.ConcurrentImages,
                SourceArtifacts = [alignment]
            };
            if (project.Settings.DepthSuppressSky)
            {
                processing.SourceArtifacts.Add(context.Artifact("semantics"));
            }
            var depth = context.Build("depth-image", inputs, directory =>
            {
                var map = depthEstimator.Estimate(store, project, scene, camera, cancellation, plan.WorkersPerImage, message =>
                {
                    lock (progressGate)
                    {
                        context.ReportProgress(Name, $"Image {index + 1}/{scene.Cameras.Count} · {photo.Name} · {message}", completed, scene.Cameras.Count);
                    }
                }, execution: processing.Executions.Add, subjectDistance: subjectDistance);
                if (project.Settings.DepthSuppressSky)
                {
                    var removed = DepthSkyFilter.Apply(map, semantics[camera.ImageId], cancellation);
                    lock (progressGate)
                    {
                        context.ReportProgress(Name, $"{photo.Name} · Removed {removed:N0} sky depth samples", completed, scene.Cameras.Count);
                    }
                }
                detected = map;
                cancellation.ThrowIfCancellationRequested();
                lock (progressGate)
                {
                    context.ReportProgress(Name, $"Image {index + 1}/{scene.Cameras.Count} · {photo.Name} · saving depth and confidence maps", completed, scene.Cameras.Count);
                }
                fileOperations.AtomicData(Path.Combine(directory, "depth.json"), map, cancellation);
            }, () => processing, algorithmVersion);
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
                    if (!reused || resumingPartialPreview)
                    {
                        PublishPreview(context, previewUnits);
                    }
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

        var originalUnits = depthUnits;
        if (project.Settings.RecoverCliffDepth)
        {
            context.BeginStep(Name, stepNames, stepIndex++, originalUnits.Length);
            depthUnits = cliffRecovery.Build(context, scene, originalUnits, semantics, subjectDistance);
        }
        context.BeginStep(Name, stepNames, stepIndex, 1);
        var artifact = context.Build("depth", depthUnits, directory =>
        {
            fileOperations.AtomicJson(Path.Combine(directory, "units.json"), depthUnits);
            fileOperations.AtomicJson(Path.Combine(directory, "subject-distance.json"), subjectDistance);
            if (project.Settings.RecoverCliffDepth)
            {
                fileOperations.AtomicJson(Path.Combine(directory, "original-units.json"), originalUnits);
            }
        }, () => new ProcessingMetadata { SourceArtifacts = depthUnits.ToList() });
        context.Publish("depth", artifact);
        context.ReportOperation(Name, new OperationProgress("Save depth result", "Depth result saved. All depth steps are complete.", 1, 1, "results"));
    }

    private void PublishPreview(RunContext context, Dictionary<string, string> units)
    {
        // Concurrent images may finish in any order. Keep explicit image IDs in
        // the preview while preserving camera order in the final depth index.
        var artifact = context.Build("depth-preview", units, directory =>
        {
            fileOperations.AtomicJson(Path.Combine(directory, "units.json"), units);
        }, () => new ProcessingMetadata { SourceArtifacts = units.Values.ToList() });
        context.Publish("depthPreview", artifact);
    }
}
