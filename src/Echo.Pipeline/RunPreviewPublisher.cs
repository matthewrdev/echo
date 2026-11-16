using Echo.Infrastructure;

namespace Echo.Pipeline;

// Two atomic JSON snapshots per stage bound disk growth and do not create checkpoints.
public sealed class RunPreviewPublisher
{
    private readonly RunContext context;
    private readonly IFileOperations files;
    private readonly string stage;
    private long lastPublished;
    private int revision;

    public RunPreviewPublisher(RunContext context, IFileOperations files, RunPreviewStage stage)
    {
        this.context = context;
        this.files = files;
        this.stage = stage.ToString().ToLowerInvariant();
        var current = context.Run.Artifacts.GetValueOrDefault(this.stage + "Preview");
        revision = current?.EndsWith("/0", StringComparison.Ordinal) == true ? 0 : 1;
    }

    public bool IsDue => lastPublished == 0 || Environment.TickCount64 - lastPublished >= 3000;

    public void Publish<T>(T preview)
    {
        context.Cancellation.ThrowIfCancellationRequested();
        revision = 1 - revision;
        var path = $"runs/{context.Run.Id}/previews/{stage}/{revision}";
        files.AtomicJson(context.Store.PathFor(path + "/preview.json"), preview);
        context.Publish(stage + "Preview", path);
        lastPublished = Environment.TickCount64;
    }
}
