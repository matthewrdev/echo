using System.Collections.Concurrent;

namespace Echo.Storage.Artifacts;

// Only newly written staging files participate. Checkpoint reuse still hashes
// persisted files independently, so this never substitutes for corruption checks.
public sealed class ArtifactWriteScope : IDisposable
{
    private readonly ConcurrentDictionary<string, WrittenArtifact> files = new(StringComparer.Ordinal);
    private readonly Action close;
    private bool disposed;

    internal ArtifactWriteScope(Action close)
    {
        this.close = close;
    }

    internal void Record(string path, string checksum)
    {
        var file = new FileInfo(path);
        files[Path.GetFullPath(path)] = new WrittenArtifact(checksum, file.Length, file.LastWriteTimeUtc, file.CreationTimeUtc);
    }

    public bool TryGetChecksum(string path, out string checksum)
    {
        checksum = string.Empty;
        if (!files.TryGetValue(Path.GetFullPath(path), out var written))
        {
            return false;
        }

        var file = new FileInfo(path);
        if (!file.Exists || file.Length != written.Length)
        {
            return false;
        }

        var unchanged = file.LastWriteTimeUtc == written.LastWriteUtc && file.CreationTimeUtc == written.CreatedUtc;
        if (!unchanged)
        {
            return false;
        }

        checksum = written.Checksum;
        return true;
    }

    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            files.Clear();
            close();
        }
    }
}
