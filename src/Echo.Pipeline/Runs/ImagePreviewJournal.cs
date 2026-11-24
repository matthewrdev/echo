using Echo.Models;
using Echo.Storage.Journals;

namespace Echo.Pipeline.Runs;

internal sealed class ImagePreviewJournal : IDisposable
{
    private readonly RunContext context;
    private readonly string key;
    private readonly string folder;
    private readonly string path;
    private readonly ImagePreviewRecordCodec codec = new();
    private readonly Dictionary<string, string> units;
    private BinaryJournal<ImagePreviewRecord>? journal;

    public ImagePreviewJournal(RunContext context, string stage)
    {
        this.context = context;
        key = stage + "Preview";
        folder = $"runs/{context.Run.Id}/previews/{stage}";
        path = context.Store.PathFor(folder + "/units.bin");
        units = codec.ReadUnits(path);
        IsResuming = units.Count > 0 && !context.Run.Artifacts.ContainsKey(stage);
    }

    public bool IsResuming
    {
        get;
    }

    public void Publish(string imageId, string artifact, bool reused)
    {
        if (units.GetValueOrDefault(imageId) != artifact)
        {
            if (reused && !IsResuming)
            {
                return;
            }

            journal ??= new BinaryJournal<ImagePreviewRecord>(path, codec);
            journal.Append(new ImagePreviewRecord(imageId, artifact));
            units[imageId] = artifact;
        }

        if (context.Run.Artifacts.GetValueOrDefault(key) != folder)
        {
            context.Publish(key, folder);
        }
    }

    public void Dispose() => journal?.Dispose();
}
