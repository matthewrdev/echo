using Echo.Models;
using Echo.Storage.Journals;

namespace Echo.Pipeline.Runs;

internal sealed class RunProcessingJournal : IDisposable
{
    public const string FileName = "processing.bin";

    private readonly string path;
    private readonly ArtifactProcessingRecordCodec codec = new();
    private BinaryJournal<ArtifactProcessingRecord>? writer;

    private long lastFlush = long.MinValue;
    private bool pending;

    public RunProcessingJournal(string directory)
    {
        path = Path.Combine(directory, FileName);
    }

    public bool Exists => File.Exists(path);

    public void Append(string artifact, ArtifactProcessingReference reference)
    {
        writer ??= new BinaryJournal<ArtifactProcessingRecord>(path, codec);
        writer.Append(new ArtifactProcessingRecord(artifact, reference), durable: false);
        pending = true;
        var now = Environment.TickCount64;
        if (lastFlush == long.MinValue || now - lastFlush >= 1000)
        {
            Flush();
        }
    }

    public void Flush()
    {
        if (pending)
        {
            writer!.Flush();
            lastFlush = Environment.TickCount64;
            pending = false;
        }
    }

    public void Dispose()
    {
        if (writer != null)
        {
            using (writer)
            {
                Flush();
            }
        }
    }
}
