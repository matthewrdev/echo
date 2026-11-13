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

    [ImportingConstructor]
    public FeatureStage(IFileOperations fileOperations, IFeatureExtractor featureExtractor, IJsonCodec jsonCodec)
    {
        this.fileOperations = fileOperations;
        this.featureExtractor = featureExtractor;
        this.jsonCodec = jsonCodec;
    }

    public string Name => "features";
    public int Order => 0;

    public void Execute(RunContext context)
    {
        var project = context.Run.Snapshot;
        var images = context.Images;
        var store = context.Store;
        var cancellation = context.Cancellation;
        var featureUnits = new Dictionary<string, string>();
        var previewCount = context.Run.Artifacts.ContainsKey("featuresPreview")
            ? context.ReadArtifact<Dictionary<string, string>>("featuresPreview", "units.json").Count
            : 0;
        foreach (var image in images)
        {
            var inputs = new
            {
                image.Id,
                image.SourceHash,
                detectorVersion = 6,
                project.Settings.ScaleInvariantFeatures,
                project.Settings.DetectLines,
                project.Settings.LineLimit,
                project.Settings.LineMinimumLength,
                project.Settings.LineGradientThreshold,
                project.Settings.LineSplitRegions,
                project.Settings.FeatureWidth,
                project.Settings.FeatureLimit
            };
            FeatureSet? detected = null;
            var unit = context.Build("feature-image", inputs, directory =>
            {
                detected = featureExtractor.Extract(store, image, project.Settings, cancellation);
                fileOperations.AtomicJson(Path.Combine(directory, "features.json"), detected);
            });
            featureUnits[image.Id] = unit;
            var reused = detected == null;
            var features = detected ?? jsonCodec.Read<FeatureSet>(store.PathFor(unit + "/features.json"));
            if (featureUnits.Count > previewCount)
            {
                PublishPreview(context, featureUnits);
                previewCount = featureUnits.Count;
            }
            var action = reused ? "Reused" : "Found";
            context.Report("imageCheckpoint", new
            {
                stage = "features",
                imageId = image.Id,
                imageName = image.Name,
                pointCount = features.Features.Count,
                lineCount = features.Lines.Count,
                reused,
                message = $"{image.Name} · {action} {features.Features.Count:N0} points and {features.Lines.Count:N0} line segments · {featureUnits.Count:N0}/{images.Count:N0} images ready",
                completed = featureUnits.Count,
                total = images.Count
            });
        }

        var artifact = context.Build("features", featureUnits, directory =>
        {
            fileOperations.AtomicJson(Path.Combine(directory, "units.json"), featureUnits);
        });
        context.Publish("features", artifact);
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
