using Microsoft.Win32.SafeHandles;

namespace Echo.Storage;

public sealed class PageFile : IDisposable
{
    private readonly string path;
    private readonly PageCache cache;
    private bool disposed;
    private bool changed;
    private readonly long originalLength;

    public PageFile(string path, PageCache cache, bool writable, PageFileLifetime lifetime = PageFileLifetime.Persistent)
    {
        this.cache = cache;
        this.path = path;
        Writable = writable;
        Lifetime = lifetime;
        if (lifetime == PageFileLifetime.Temporary && !writable)
        {
            throw new ArgumentException("Temporary paging files must be writable.", nameof(writable));
        }
        using var handle = File.OpenHandle(path, lifetime == PageFileLifetime.Temporary ? FileMode.CreateNew : writable ? FileMode.OpenOrCreate : FileMode.Open,
            writable ? FileAccess.ReadWrite : FileAccess.Read, FileShare.Read);
        Length = RandomAccess.GetLength(handle);
        originalLength = Length;
        MaterializedLength = Length;
    }

    internal PageCache Cache => cache;

    internal bool RetainReleasedBuffers
    {
        get; init;
    }

    public PageFileLifetime Lifetime
    {
        get;
    }
    internal long MaterializedLength
    {
        get; private set;
    }

    internal long AllocatedBytes => Length - originalLength;

    public long Length
    {
        get; private set;
    }
    public bool Writable
    {
        get;
    }

    public PageMemoryReservation? TryReserveMemory(long bytes)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return cache.TryReserveMemory(bytes);
    }

    public void ReadBytes(long offset, Span<byte> bytes)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (offset < 0 || offset > Length - bytes.Length)
        {
            throw new EndOfStreamException("Truncated paged record.");
        }
        cache.Read(this, offset, bytes);
    }

    public void WriteBytes(long offset, ReadOnlySpan<byte> bytes)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!Writable || offset < 0)
        {
            throw new InvalidOperationException("Cannot modify this paged file.");
        }
        changed = true;
        cache.Write(this, offset, bytes);
    }

    public void Flush()
    {
        cache.Release(this);
        if (Writable && changed)
        {
            using var handle = Open();
            RandomAccess.FlushToDisk(handle);
            changed = false;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        try
        {
            if (Lifetime == PageFileLifetime.Temporary)
            {
                cache.Release(this, discard: true);
                File.Delete(path);
                cache.ReleaseSequentialScratch(AllocatedBytes);
            }
            else
            {
                Flush();
            }
        }
        finally
        {
            disposed = true;
        }
    }

    internal void SetLength(long length)
    {
        var handle = cache.Handle(this);
        RandomAccess.SetLength(handle, length);
        Length = length;
    }

    internal void Read(long offset, Span<byte> bytes)
    {
        var handle = cache.Handle(this);
        while (!bytes.IsEmpty)
        {
            var count = RandomAccess.Read(handle, bytes, offset);
            if (count == 0)
            {
                throw new EndOfStreamException("Truncated paging file.");
            }
            bytes = bytes[count..];
            offset += count;
        }
    }

    internal void Write(long offset, ReadOnlySpan<byte> bytes)
    {
        var handle = cache.Handle(this);
        RandomAccess.Write(handle, bytes, offset);
        MaterializedLength = Math.Max(MaterializedLength, offset + bytes.Length);
    }

    internal SafeFileHandle Open() => File.OpenHandle(path, FileMode.Open,
        Writable ? FileAccess.ReadWrite : FileAccess.Read, FileShare.Read);
}
