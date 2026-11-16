using System.Buffers;

namespace Echo.Storage;

// Sequential scratch records deliberately bypass the random-access page cache.
// Their live disk allocation and I/O still count against the same session budget.
public sealed class TemporaryRecordFile<T>
{
    private readonly string path;
    private readonly RecordCodec<T> codec;
    private readonly PageCache cache;
    private long reserved;
    private bool created;

    internal TemporaryRecordFile(string path, RecordCodec<T> codec, PageCache cache)
    {
        this.path = path;
        this.codec = codec;
        this.cache = cache;
    }

    public long Count
    {
        get; private set;
    }

    internal void Write(IEnumerable<T> records)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.SequentialScan);
        created = true;
        var bytes = ArrayPool<byte>.Shared.Rent(Math.Max(65536, codec.Size));
        try
        {
            var capacity = bytes.Length / codec.Size * codec.Size;
            var position = 0;
            foreach (var record in records)
            {
                codec.Encode(record, bytes.AsSpan(position, codec.Size));
                position += codec.Size;
                Count++;
                if (position == capacity)
                {
                    WriteBlock(stream, bytes.AsSpan(0, position));
                    position = 0;
                }
            }
            if (position > 0)
            {
                WriteBlock(stream, bytes.AsSpan(0, position));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    public IEnumerable<T> Read()
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
        var bytes = ArrayPool<byte>.Shared.Rent(Math.Max(65536, codec.Size));
        try
        {
            var remaining = Count;
            while (remaining > 0)
            {
                var count = (int)Math.Min(remaining, bytes.Length / codec.Size);
                stream.ReadExactly(bytes.AsSpan(0, count * codec.Size));
                cache.RecordSequentialIo(count * (long)codec.Size, 0);
                for (var index = 0; index < count; index++)
                {
                    yield return codec.Decode(bytes.AsSpan(index * codec.Size, codec.Size));
                }
                remaining -= count;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    internal void Delete()
    {
        if (created)
        {
            File.Delete(path);
            created = false;
        }
        cache.ReleaseSequentialScratch(reserved);
        reserved = 0;
    }

    private void WriteBlock(Stream stream, ReadOnlySpan<byte> bytes)
    {
        cache.ReserveSequentialScratch(bytes.Length);
        reserved += bytes.Length;
        stream.Write(bytes);
        cache.RecordSequentialIo(0, bytes.Length);
    }
}
