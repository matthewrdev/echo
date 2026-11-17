using System.Buffers;

namespace Echo.Storage;

// Scratch runs keep a bounded resident prefix and spill only the remaining
// suffix. Overlapping merge cursors may share cached pages of spilled runs.
public sealed class TemporaryRecordFile<T>
{
    private readonly string path;
    private readonly RecordCodec<T> codec;
    private readonly PageCache cache;
    private long reserved;
    private long residentCount;
    private bool created;
    private readonly bool preferResident;
    private readonly List<ResidentScratchBlock> blocks = [];
    private PageFile? sharedFile;
    private readonly object readGate = new();

    internal TemporaryRecordFile(string path, RecordCodec<T> codec, PageCache cache, bool preferResident)
    {
        this.path = path;
        this.codec = codec;
        this.cache = cache;
        this.preferResident = preferResident;
    }

    public long Count
    {
        get; private set;
    }

    internal void Write(IEnumerable<T> records)
    {
        FileStream? stream = null;
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
                    StoreBlock(ref stream, bytes.AsSpan(0, position));
                    position = 0;
                }
            }
            if (position > 0)
            {
                StoreBlock(ref stream, bytes.AsSpan(0, position));
            }
        }
        finally
        {
            try
            {
                stream?.Dispose();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bytes);
            }
        }
    }

    public IEnumerable<T> Read()
    {
        foreach (var record in ReadResident())
        {
            yield return record;
        }
        if (!created)
        {
            yield break;
        }
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
        var bytes = ArrayPool<byte>.Shared.Rent(Math.Max(65536, codec.Size));
        try
        {
            var remaining = Count - residentCount;
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

    public IEnumerable<T> ReadShared()
    {
        foreach (var record in ReadResident())
        {
            yield return record;
        }
        if (!created)
        {
            yield break;
        }
        PageFile source;
        lock (readGate)
        {
            source = sharedFile ??= new PageFile(path, cache, false)
            {
                RetainReleasedBuffers = true
            };
        }
        var bytes = ArrayPool<byte>.Shared.Rent(Math.Max(65536, codec.Size));
        try
        {
            for (long first = 0; first < Count - residentCount;)
            {
                var count = (int)Math.Min(Count - residentCount - first, bytes.Length / codec.Size);
                source.ReadBytes(first * codec.Size, bytes.AsSpan(0, count * codec.Size));
                for (var index = 0; index < count; index++)
                {
                    yield return codec.Decode(bytes.AsSpan(index * codec.Size, codec.Size));
                }
                first += count;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    internal void Delete()
    {
        sharedFile?.Dispose();
        sharedFile = null;
        ReleaseMemory();
        if (created)
        {
            File.Delete(path);
            created = false;
        }
        cache.ReleaseSequentialScratch(reserved);
        reserved = 0;
    }

    private void StoreBlock(ref FileStream? stream, ReadOnlySpan<byte> bytes)
    {
        if (preferResident && !created && TryRetainBlock(bytes))
        {
            return;
        }
        if (stream == null)
        {
            stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.SequentialScan);
            created = true;
        }
        WriteBlock(stream, bytes);
    }

    private bool TryRetainBlock(ReadOnlySpan<byte> bytes)
    {
        var block = cache.TryRentScratchBlock(bytes.Length);
        if (block == null)
        {
            return false;
        }
        try
        {
            bytes.CopyTo(block.Bytes);
            blocks.Add(block);
            residentCount += bytes.Length / codec.Size;
            return true;
        }
        catch
        {
            block.Dispose();
            throw;
        }
    }

    private IEnumerable<T> ReadResident()
    {
        foreach (var block in blocks)
        {
            for (var offset = 0; offset < block.Length; offset += codec.Size)
            {
                yield return codec.Decode(block.Bytes.AsSpan(offset, codec.Size));
            }
        }
    }

    private void ReleaseMemory()
    {
        foreach (var block in blocks)
        {
            block.Dispose();
        }
        blocks.Clear();
        residentCount = 0;
    }

    private void WriteBlock(Stream stream, ReadOnlySpan<byte> bytes)
    {
        cache.ReserveSequentialScratch(bytes.Length);
        reserved += bytes.Length;
        stream.Write(bytes);
        cache.RecordSequentialIo(0, bytes.Length);
    }
}
