using Echo.Infrastructure;
using Echo.Models;

namespace Echo.Pipeline;

// Two atomic snapshots per stage bound disk growth and do not create checkpoints.
public sealed class RunPreviewPublisher
{
    private readonly RunContext context;
    private readonly IFileOperations files;
    private readonly string stage;
    private long lastPublished;
    private long publishInterval = 3000;
    private int revision;

    public RunPreviewPublisher(RunContext context, IFileOperations files, RunPreviewStage stage)
    {
        this.context = context;
        this.files = files;
        this.stage = stage.ToString().ToLowerInvariant();
        var current = context.Run.Artifacts.GetValueOrDefault(this.stage + "Preview");
        revision = current?.EndsWith("/0", StringComparison.Ordinal) == true ? 0 : 1;
    }

    public bool IsDue => lastPublished == 0 || Environment.TickCount64 - lastPublished >= publishInterval;

    public void Publish<T>(T preview)
    {
        PublishCore(path => files.AtomicJson(path + "/preview.json", preview));
    }

    public void PublishAlignment(AlignmentPreview preview, IAlignmentPreviewWriter writer)
    {
        PublishCore(path => writer.Write(path + "/preview.bin", preview, context.Cancellation));
    }

    private void PublishCore(Action<string> write)
    {
        context.Cancellation.ThrowIfCancellationRequested();
        var started = Environment.TickCount64;
        revision = 1 - revision;
        var path = $"runs/{context.Run.Id}/previews/{stage}/{revision}";
        write(context.Store.PathFor(path));
        context.Publish(stage + "Preview", path);
        lastPublished = Environment.TickCount64;
        // Keep publication below roughly 10% of elapsed processing time on slow disks.
        publishInterval = Math.Max(3000, (lastPublished - started) * 9);
    }
}
