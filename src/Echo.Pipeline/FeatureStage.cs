using Echo.Compute;
using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Features;
using Echo.Infrastructure;

namespace Echo.Pipeline;

[Export(typeof(IPipelineStage)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class FeatureStage : IPipelineStage
{
    private readonly IFileOperations fileOperations;
    private readonly IFeatureExtractor featureExtractor;
    private readonly IJsonCodec jsonCodec;
    private readonly IImageWorkPlanner workPlanner;
    private readonly IOrderedWorkExecutor workExecutor;

    [ImportingConstructor]
    public FeatureStage(IFileOperations fileOperations, IFeatureExtractor featureExtractor, IJsonCodec jsonCodec, IImageWorkPlanner workPlanner, IOrderedWorkExecutor workExecutor)
    {
        this.fileOperations = fileOperations;
        this.featureExtractor = featureExtractor;
        this.jsonCodec = jsonCodec;
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
        var previewCount = context.Run.Artifacts.ContainsKey("featuresPreview")
            ? context.ReadArtifact<Dictionary<string, string>>("featuresPreview", "units.json").Count
            : 0;
        var plan = workPlanner.Features(project.Settings, images);
        workExecutor.Run(images.Count, plan.Workers, index => BuildImage(context, images[index]), (index, checkpoint) =>
        {
            var image = images[index];
            featureUnits[image.Id] = checkpoint.Artifact;
            if (!checkpoint.Reused && featureUnits.Count > previewCount)
            {
                PublishPreview(context, featureUnits);
                previewCount = featureUnits.Count;
            }
            var action = checkpoint.Reused ? "Reused" : "Found";
            context.Report("imageCheckpoint", new
            {
                stage = "features",
                imageId = image.Id,
                imageName = image.Name,
                pointCount = checkpoint.PointCount,
                lineCount = checkpoint.LineCount,
                reused = checkpoint.Reused,
                message = $"{image.Name} · {action} {checkpoint.PointCount:N0} points and {checkpoint.LineCount:N0} line segments · {featureUnits.Count:N0}/{images.Count:N0} images ready",
                completed = featureUnits.Count,
                total = images.Count
            });
        }, cancellation);

        var artifact = context.Build("features", featureUnits, directory =>
        {
            fileOperations.AtomicJson(Path.Combine(directory, "units.json"), featureUnits);
        });
        context.Publish("features", artifact);
    }

    private FeatureImageCheckpoint BuildImage(RunContext context, Photo image)
    {
        var settings = context.Run.Snapshot.Settings;
        var inputs = new
        {
            image.Id,
            image.SourceHash,
            detectorVersion = 7,
            settings.ComputeBackend,
            computeImplementation = context.ComputeImplementation(ComputeOperation.Features),
            settings.ScaleInvariantFeatures,
            settings.DetectLines,
            settings.LineLimit,
            settings.LineMinimumLength,
            settings.LineGradientThreshold,
            settings.LineSplitRegions,
            settings.FeatureWidth,
            settings.FeatureLimit
        };
        FeatureSet? detected = null;
        var unit = context.Build("feature-image", inputs, directory =>
        {
            context.Trace.Operation(new OperationProgress("Feature extraction", image.Name));
            detected = featureExtractor.Extract(context.Store, image, settings, context.Cancellation);
            context.Trace.Operation(new OperationProgress("Save feature descriptors", image.Name, detected.Features.Count, detected.Features.Count, "features"));
            fileOperations.AtomicJson(Path.Combine(directory, "features.json"), detected);
        });
        var features = detected ?? jsonCodec.Read<FeatureSet>(context.Store.PathFor(unit + "/features.json"));
        return new FeatureImageCheckpoint(unit, features.Features.Count, features.Lines.Count, detected == null);
    }

    private void PublishPreview(RunContext context, Dictionary<string, string> featureUnits)
    {
        // Publish only committed image checkpoints. Each index is immutable, so
        // an open viewer remains safe while the next image is being processed.
        var artifact = context.Build("features-preview", featureUnits, directory =>
        {
            fileOperations.AtomicJson(Path.Combine(directory, "units.json"), featureUnits);
        });
        context.Publish("featuresPreview", artifact);
    }
}
