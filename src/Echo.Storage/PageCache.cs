using Echo.Contracts;

namespace Echo.Storage;

// One cache is shared by every paged file in a processing session. No caller can
// retain a cache page: access copies just the requested record under the lock.
public sealed class PageCache : IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<PageAddress, LinkedListNode<CachedPage>> pages = [];
    private readonly LinkedList<CachedPage> recent = [];
    private readonly long capacity;
    private readonly long diskLimit;
    private readonly int pageSize;
    private long peak;
    private long reads;
    private long writes;
    private long hits;
    private long misses;
    private long evictions;
    private long scratch;
    private bool disposed;

    public PageCache(long capacity, long diskLimit, int pageSize = 65536)
    {
        if (pageSize < 4096 || capacity < pageSize || diskLimit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }
        this.capacity = capacity;
        this.diskLimit = diskLimit;
        this.pageSize = pageSize;
    }

    public PagingStatistics Statistics
    {
        get
        {
            lock (gate)
            {
                return new(pages.Count * (long)pageSize, peak, reads, writes, hits, misses, evictions, scratch);
            }
        }
    }

    internal void Grow(PageFile file, long length)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            length = checked((length + pageSize - 1) / pageSize * pageSize);
            var growth = length - file.Length;
            if (growth <= 0)
            {
                return;
            }
            if (file.Writable && diskLimit != 0 && growth > diskLimit - scratch)
            {
                throw new EchoException("Paging scratch-disk budget exceeded. Completed checkpoints are retained; increase ProcessingDiskBudgetMb and resume.", 5);
            }
            file.SetLength(length);
            if (file.Writable)
            {
                scratch += growth;
            }
        }
    }

    internal void Read(PageFile file, long offset, Span<byte> destination)
    {
        lock (gate)
        {
            while (!destination.IsEmpty)
            {
                var page = Get(file, offset / pageSize);
                var start = (int)(offset % pageSize);
                var count = Math.Min(destination.Length, pageSize - start);
                page.Bytes.AsSpan(start, count).CopyTo(destination);
                destination = destination[count..];
                offset += count;
            }
        }
    }

    internal void Write(PageFile file, long offset, ReadOnlySpan<byte> source)
    {
        lock (gate)
        {
            Grow(file, checked(offset + source.Length));
            while (!source.IsEmpty)
            {
                var page = Get(file, offset / pageSize);
                var start = (int)(offset % pageSize);
                var count = Math.Min(source.Length, pageSize - start);
                source[..count].CopyTo(page.Bytes.AsSpan(start, count));
                page.Dirty = true;
                source = source[count..];
                offset += count;
            }
        }
    }

    internal void Release(PageFile file)
    {
        lock (gate)
        {
            var node = recent.First;
            while (node != null)
            {
                var next = node.Next;
                if (node.Value.Address.File == file)
                {
                    Flush(node.Value);
                    pages.Remove(node.Value.Address);
                    recent.Remove(node);
                }
                node = next;
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
            foreach (var page in recent)
            {
                Flush(page);
            }
            recent.Clear();
            pages.Clear();
            disposed = true;
        }
    }

    private CachedPage Get(PageFile file, long index)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var address = new PageAddress(file, index);
        if (pages.TryGetValue(address, out var existing))
        {
            hits++;
            recent.Remove(existing);
            recent.AddFirst(existing);
            return existing.Value;
        }
        misses++;
        byte[] bytes;
        if ((pages.Count + 1L) * pageSize > capacity)
        {
            var oldest = recent.Last!;
            Flush(oldest.Value);
            pages.Remove(oldest.Value.Address);
            recent.RemoveLast();
            bytes = oldest.Value.Bytes;
            Array.Clear(bytes);
            evictions++;
        }
        else
        {
            bytes = new byte[pageSize];
        }
        var offset = index * pageSize;
        var count = (int)Math.Min(pageSize, Math.Max(0, file.Length - offset));
        file.Read(offset, bytes.AsSpan(0, count));
        reads += count;
        var page = new CachedPage { Address = address, Bytes = bytes };
        pages.Add(address, recent.AddFirst(page));
        peak = Math.Max(peak, pages.Count * (long)pageSize);
        return page;
    }

    private void Flush(CachedPage page)
    {
        if (!page.Dirty)
        {
            return;
        }
        var offset = page.Address.Index * pageSize;
        var count = (int)Math.Min(pageSize, page.Address.File.Length - offset);
        page.Address.File.Write(offset, page.Bytes.AsSpan(0, count));
        writes += count;
        page.Dirty = false;
    }
}
