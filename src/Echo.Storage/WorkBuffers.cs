using Echo.Contracts;

namespace Echo.Storage;

public sealed class WorkBuffers : IDisposable
{
    private readonly BulkDataSession session;
    private readonly DiskCollections dictionaries;
    private readonly string directory;
    private int nextFile;
    private long indexMemory;

    public WorkBuffers(BulkDataSession session, string directory, long cacheBytes, long diskBytes)
    {
        this.session = session;
        this.directory = directory;
        Directory.CreateDirectory(directory);
        indexMemory = cacheBytes / 2;
        dictionaries = new DiskCollections(Path.Combine(directory, "indices.sqlite"), cacheBytes - indexMemory, diskBytes);
    }

    public long IndexDiskBytes => dictionaries.DiskBytes;

    public long ReserveIndexMemory(long maximumBytes)
    {
        if (maximumBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }
        var reserved = Math.Min(indexMemory, maximumBytes);
        indexMemory -= reserved;
        return reserved;
    }

    public IDictionary<TKey, TValue> Map<TKey, TValue>(RecordCodec<TKey> keys, RecordCodec<TValue> values) where TKey : notnull
    {
        var reservation = indexMemory / 2;
        indexMemory -= reservation;
        // Includes Dictionary capacity slack, entry headers and managed objects.
        var recordBytes = Math.Max(512L, (keys.Size + (long)values.Size) * 4 + 128);
        var capacity = (int)Math.Min(int.MaxValue, reservation / recordBytes);
        return new SpillDictionary<TKey, TValue>(capacity, () => dictionaries.Create(keys, values));
    }

    public IList<T> List<T>(RecordCodec<T> codec)
        => session.Open(Path.Combine(directory, $"buffer-{nextFile++}.bin"), codec, true);

    public IList<T> Filled<T>(int count, T value, RecordCodec<T> codec)
    {
        var result = List(codec);
        for (var index = 0; index < count; index++)
        {
            result.Add(value);
        }
        return result;
    }

    public Surface Surface() => new()
    {
        Vertices = List(new VectorRecordCodec()),
        Faces = List(new TriangleRecordCodec()),
        SourceImages = List(new StringTableCodec())
    };

    public void Dispose() => dictionaries.Dispose();
}
