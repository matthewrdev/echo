using System.Text;
using Echo.Infrastructure;
using Echo.Models;
using Echo.Pipeline.Alignment;
using Echo.Storage.Artifacts;

namespace Echo.Pipeline.Runs;

// Atomic replacement keeps each published preview self-contained.
public sealed class RunPreviewPublisher
{
    private readonly RunContext context;
    private readonly IFileOperations files;
    private readonly string stage;
    private long lastPublished;
    private long publishInterval = 3000;

    public RunPreviewPublisher(RunContext context, IFileOperations files, RunPreviewStage stage)
    {
        this.context = context;
        this.files = files;
        this.stage = stage.ToString().ToLowerInvariant();
    }

    public bool IsDue => lastPublished == 0 || Environment.TickCount64 - lastPublished >= publishInterval;

    public void PublishFusion(FusionPreview preview)
    {
        PublishCore(path => files.AtomicWrite(path + "/preview.bin", stream =>
        {
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            new FusionPreviewCodec().Write(writer, preview);
        }));
    }

    public void PublishCloud(DenseCloudReport preview)
    {
        PublishCore(path => files.AtomicWrite(path + "/cloud.bin", stream =>
        {
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            new DenseCloudReportCodec().Write(writer, preview);
        }));
    }

    public void PublishAlignment(AlignmentPreview preview, IAlignmentPreviewWriter writer)
    {
        PublishCore(path => writer.Write(path + "/preview.bin", preview, context.Cancellation));
    }

    private void PublishCore(Action<string> write)
    {
        context.Cancellation.ThrowIfCancellationRequested();
        var started = Environment.TickCount64;
        var path = $"runs/{context.Run.Id}/previews/{stage}";
        write(context.Store.PathFor(path));
        context.PublishPreview(stage + "Preview", path);
        lastPublished = Environment.TickCount64;
        // Keep publication below roughly 10% of elapsed processing time on slow disks.
        publishInterval = Math.Max(3000, (lastPublished - started) * 9);
    }
}
