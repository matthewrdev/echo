using Microsoft.Win32.SafeHandles;

namespace Echo.Storage;

public sealed class PageFile : IDisposable
{
    private readonly string path;
    private readonly PageCache cache;
    private bool disposed;

    public PageFile(string path, PageCache cache, bool writable)
    {
        this.cache = cache;
        this.path = path;
        Writable = writable;
        using var handle = File.OpenHandle(path, writable ? FileMode.OpenOrCreate : FileMode.Open,
            writable ? FileAccess.ReadWrite : FileAccess.Read, FileShare.Read);
        Length = RandomAccess.GetLength(handle);
    }

    public long Length
    {
        get; private set;
    }
    public bool Writable
    {
        get;
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
        cache.Write(this, offset, bytes);
    }

    public void Flush()
    {
        cache.Release(this);
        if (Writable)
        {
            using var handle = Open();
            RandomAccess.FlushToDisk(handle);
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
            Flush();
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
    }

    internal SafeFileHandle Open() => File.OpenHandle(path, FileMode.Open,
        Writable ? FileAccess.ReadWrite : FileAccess.Read, FileShare.Read);
}
