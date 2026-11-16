using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.DenseCloud;
using Echo.Infrastructure;
using Echo.Storage;

namespace Echo.Pipeline;

[Export(typeof(IPipelineStage)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class DenseCloudStage : IPipelineStage
{
    private readonly IFileOperations fileOperations;
    private readonly IJsonCodec jsonCodec;
    private readonly ICloudBuilder cloudBuilder;

    [ImportingConstructor]
    public DenseCloudStage(IFileOperations fileOperations, IJsonCodec jsonCodec, ICloudBuilder cloudBuilder)
    {
        this.fileOperations = fileOperations;
        this.jsonCodec = jsonCodec;
        this.cloudBuilder = cloudBuilder;
    }

    public string Name => "cloud";
    public int Order => 4;

    public void Execute(RunContext context)
    {
        var alignment = context.Artifact("alignment");
        var depthUnits = context.DepthUnits();
        var settings = context.Run.Snapshot.Settings;
        var inputs = new
        {
            depthUnits,
            alignment,
            cloudVersion = 4,
            settings.ComputeBackend,
            context.ComputeImplementation,
            settings.CloudNeighbours,
            settings.CloudMinimumSupport,
            settings.CloudDepthTolerance,
            settings.CloudReprojectionTolerancePixels,
            settings.CloudMinimumParallaxDegrees
        };
        var cloudPath = context.Build(Name, inputs, directory =>
        {
            var maps = LoadDepthMaps(context, depthUnits);
            var names = context.Run.Snapshot.Images.ToDictionary(image => image.Id, image => image.Name);
            var cloud = cloudBuilder.Build(
                context.Store, context.Run.Snapshot, context.Scene(), maps, context.Cancellation,
                progress => context.ReportOperation(Name, new OperationProgress(
                    $"Dense cloud validation · {names[progress.ImageId]}",
                    $"Image {progress.ImageNumber:N0} of {progress.TotalImages:N0} · {progress.RetainedPoints:N0} points accepted",
                    progress.CompletedRows, progress.TotalRows, "rows")),
                preview => PublishPreview(context, inputs, preview), context.Work?.List(new DenseRecordCodec()));

            context.ReportProgress(Name, $"Saving {cloud.Samples.Count:N0} coloured points to the complete point cloud…");
            cloudBuilder.Write(Path.Combine(directory, "cloud.ply"), cloud);
            fileOperations.AtomicJson(Path.Combine(directory, "cloud.json"), cloudBuilder.Report(cloud));
            context.ReportProgress(Name, $"Saved {cloud.Samples.Count:N0} points from {maps.Count:N0} images.", maps.Count, maps.Count);
        });
        context.Publish(Name, cloudPath);
    }

    private List<DepthMap> LoadDepthMaps(RunContext context, IReadOnlyList<string> units)
    {
        var maps = new List<DepthMap>();
        foreach (var unit in units)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            context.ReportProgress(Name, $"Loading depth map {maps.Count + 1:N0} of {units.Count:N0}…", maps.Count, units.Count);
            maps.Add(context.ReadData<DepthMap>(context.Store.PathFor(unit + "/depth.json")));
        }

        return maps;
    }

    private void PublishPreview(RunContext context, object inputs, DenseCloudReport preview)
    {
        // A preview is immutable and inspectable, but is not a resumable partial cloud.
        // The final PLY remains the complete output of this stage.
        var artifact = context.Build("cloud-preview", new
        {
            previewVersion = 2,
            inputs,
            preview.InputSamples,
            preview.RetainedSamples
        }, directory =>
        {
            fileOperations.AtomicJson(Path.Combine(directory, "cloud.json"), preview);
        });
        context.Publish("cloudPreview", artifact);
    }
}
