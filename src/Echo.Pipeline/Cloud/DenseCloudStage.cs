using System.Text;
using Echo.Storage.Artifacts;
using Echo.Compute;
using Echo.DenseCloud;
using Echo.Infrastructure;
using Echo.Models;
using Echo.Pipeline.Runs;
using Echo.Storage.Records;
using Echo.Storage;

namespace Echo.Pipeline.Cloud;

public sealed class DenseCloudStage : IPipelineStage
{
    private readonly IFileOperations fileOperations;
    private readonly IJsonCodec jsonCodec;
    private readonly ICloudBuilder cloudBuilder;

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
        var implementation = context.ComputeImplementation(ComputeOperation.Cloud);
        var inputs = new DenseCloudInputs(depthUnits, alignment, settings, implementation);
        var previews = context.Headless ? null : new RunPreviewPublisher(context, fileOperations, RunPreviewStage.Cloud);
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
                previews == null ? null : previews.PublishCloud, context.Work?.List(new DenseRecordCodec()));

            context.ReportProgress(Name, $"Saving {cloud.Samples.Count:N0} coloured points to the complete point cloud…");
            cloudBuilder.Write(Path.Combine(directory, "cloud.ply"), cloud);
            fileOperations.AtomicWrite(Path.Combine(directory, "cloud.bin"), stream =>
            {
                using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
                new DenseCloudReportCodec().Write(writer, cloudBuilder.Report(cloud));
            });
            context.ReportProgress(Name, $"Saved {cloud.Samples.Count:N0} points from {maps.Count:N0} images.", maps.Count, maps.Count);
        }, sources: [alignment, .. depthUnits]);
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
}
