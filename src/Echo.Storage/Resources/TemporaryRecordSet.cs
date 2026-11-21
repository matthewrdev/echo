using Echo.Storage.Pages;
using Echo.Storage.Records;
namespace Echo.Storage.Resources;

public sealed class TemporaryRecordSet<T> : IDisposable
{
    private readonly string directory;
    private readonly RecordCodec<T> codec;
    private readonly PageCache cache;
    private readonly HashSet<TemporaryRecordFile<T>> files = [];
    private readonly object gate = new();
    private int sequence;
    private bool disposed;
    private readonly bool preferResident;

    internal TemporaryRecordSet(string directory, RecordCodec<T> codec, PageCache cache, bool preferResident)
    {
        this.directory = Path.Combine(directory, "sort-" + Guid.NewGuid().ToString("N"));
        this.codec = codec;
        this.cache = cache;
        this.preferResident = preferResident;
        Directory.CreateDirectory(this.directory);
    }

    public TemporaryRecordFile<T> Write(IEnumerable<T> records)
    {
        TemporaryRecordFile<T> file;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            file = new TemporaryRecordFile<T>(Path.Combine(directory, $"run-{sequence++}.bin"), codec, cache, preferResident);
            files.Add(file);
        }
        // Each worker owns its writer; the lock only protects file ownership.
        file.Write(records);
        return file;
    }

    public void Delete(TemporaryRecordFile<T> file)
    {
        lock (gate)
        {
            if (files.Contains(file))
            {
                file.Delete();
                files.Remove(file);
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            foreach (var file in files)
            {
                file.Delete();
            }
            files.Clear();
            Directory.Delete(directory);
            disposed = true;
        }
    }
}
