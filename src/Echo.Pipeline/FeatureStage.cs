using System.ComponentModel.Composition;
using Echo.Features;
using Echo.Infrastructure;

namespace Echo.Pipeline;

[Export(typeof(IPipelineStage)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class FeatureStage : IPipelineStage
{
    private readonly IFileOperations fileOperations;
    private readonly IFeatureExtractor featureExtractor;

    [ImportingConstructor]
    public FeatureStage(IFileOperations fileOperations, IFeatureExtractor featureExtractor)
    {
        this.fileOperations = fileOperations;
        this.featureExtractor = featureExtractor;
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
        foreach (var image in images)
        {
            var inputs = new
            {
                image.Id,
                image.SourceHash,
                detectorVersion = 3,
                project.Settings.DetectLines,
                project.Settings.LineLimit,
                project.Settings.LineMinimumLength,
                project.Settings.FeatureWidth,
                project.Settings.FeatureLimit
            };
            var unit = context.Build("feature-image", inputs, directory =>
            {
                var features = featureExtractor.Extract(store, image, project.Settings, cancellation);
                fileOperations.AtomicJson(Path.Combine(directory, "features.json"), features);
            });
            featureUnits[image.Id] = unit;
            context.Report("imageCheckpoint", new
            {
                stage = "features",
                imageId = image.Id,
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
}
