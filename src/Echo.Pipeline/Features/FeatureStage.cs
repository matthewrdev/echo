using Echo.Compute;
using Echo.Features;
using Echo.Infrastructure;
using Echo.Models;
using Echo.Pipeline.Runs;
using Echo.Storage.Matching;

namespace Echo.Pipeline.Features;

public sealed class FeatureStage : IPipelineStage
{
    private readonly IFileOperations fileOperations;
    private readonly IFeatureExtractor featureExtractor;
    private readonly IFeatureArtifactStore featureArtifacts;
    private readonly IImageWorkPlanner workPlanner;
    private readonly IOrderedWorkExecutor workExecutor;

    public FeatureStage(IFileOperations fileOperations, IFeatureExtractor featureExtractor, IFeatureArtifactStore featureArtifacts, IImageWorkPlanner workPlanner, IOrderedWorkExecutor workExecutor)
    {
        this.fileOperations = fileOperations;
        this.featureExtractor = featureExtractor;
        this.featureArtifacts = featureArtifacts;
        this.workPlanner = workPlanner;
        this.workExecutor = workExecutor;
    }

    public string Name => "features";
    public int Order => 0;

    public void Execute(RunContext context)
    {
        var project = context.Run.Snapshot;
        var images = context.Images;
        var cancellation = context.Cancellation;
        var featureUnits = new Dictionary<string, string>();
        using var previews = context.Headless ? null : new ImagePreviewJournal(context, Name);
        var plan = workPlanner.Features(project.Settings, images);
        workExecutor.Run(images.Count, plan.Workers, index => BuildImage(context, images[index]), (index, checkpoint) =>
        {
            var image = images[index];
            featureUnits[image.Id] = checkpoint.Artifact;
            previews?.Publish(image.Id, checkpoint.Artifact, checkpoint.Reused);

            var action = checkpoint.Reused ? "Reused" : "Found";
            context.Report("imageCheckpoint", new Dictionary<string, object?>
            {
                ["stage"] = "features",
                ["imageId"] = image.Id,
                ["imageName"] = image.Name,
                ["pointCount"] = checkpoint.PointCount,
                ["lineCount"] = checkpoint.LineCount,
                ["reused"] = checkpoint.Reused,
                ["message"] = $"{image.Name} · {action} {checkpoint.PointCount:N0} points and {checkpoint.LineCount:N0} line segments · {featureUnits.Count:N0}/{images.Count:N0} images ready",
                ["completed"] = featureUnits.Count,
                ["total"] = images.Count
            });
        }, cancellation);

        var artifact = context.Build("features", featureUnits, directory =>
        {
            fileOperations.AtomicJson(Path.Combine(directory, "units.json"), featureUnits);
        }, sources: featureUnits.Values);
        context.Publish("features", artifact);
    }

    private FeatureImageCheckpoint BuildImage(RunContext context, Photo image)
    {
        var settings = context.Run.Snapshot.Settings;
        var inputs = new Dictionary<string, object?>
        {
            ["id"] = image.Id,
            ["sourceHash"] = image.SourceHash,
            ["detectorVersion"] = 7,
            ["computeBackend"] = settings.ComputeBackend,
            ["computeImplementation"] = context.ComputeImplementation(ComputeOperation.Features),
            ["scaleInvariantFeatures"] = settings.ScaleInvariantFeatures,
            ["detectLines"] = settings.DetectLines,
            ["lineLimit"] = settings.LineLimit,
            ["lineMinimumLength"] = settings.LineMinimumLength,
            ["lineGradientThreshold"] = settings.LineGradientThreshold,
            ["lineSplitRegions"] = settings.LineSplitRegions,
            ["featureWidth"] = settings.FeatureWidth,
            ["featureLimit"] = settings.FeatureLimit
        };
        FeatureSet? detected = null;
        var unit = context.Build("feature-image", inputs, directory =>
        {
            context.Trace.Operation(new OperationProgress("Feature extraction", image.Name));
            detected = featureExtractor.Extract(context.Store, image, settings, context.Cancellation);
            context.Trace.Operation(new OperationProgress("Save feature descriptors", image.Name, detected.Features.Count, detected.Features.Count, "features"));
            fileOperations.AtomicJson(Path.Combine(directory, "features.json"), detected);
        });
        if (detected != null)
        {
            return new FeatureImageCheckpoint(unit, detected.Features.Count, detected.Lines.Count, Reused: false);
        }

        var header = featureArtifacts.Header(context.Store.PathFor(unit + "/features.json"));
        return new FeatureImageCheckpoint(unit, header.PointCount, header.LineCount, Reused: true);
    }
}
