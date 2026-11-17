using Echo.Contracts;
using Microsoft.Win32.SafeHandles;

namespace Echo.Storage;

// One cache is shared by every paged file in a processing session. No caller can
// retain a cache page: access copies just the requested record under the lock.
public sealed class PageCache : IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<PageAddress, LinkedListNode<CachedPage>> pages = [];
    private readonly LinkedList<CachedPage> recent = [];
    private readonly Dictionary<PageFile, LinkedListNode<CachedFileHandle>> handles = [];
    private readonly LinkedList<CachedFileHandle> recentHandles = [];
    private long handleOpens;
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
    private long reservedMemory;
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
                return new(pages.Count * (long)pageSize + reservedMemory, peak, reads, writes, hits, misses, evictions, scratch);
            }
        }
    }

    // Read-only working sets can replace cached pages while staying inside the
    // same resident budget. Leave at least one page for ongoing file access.
    public PageMemoryReservation? TryReserveMemory(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (bytes > capacity - reservedMemory - pageSize)
            {
                return null;
            }
            while (pages.Count * (long)pageSize > capacity - reservedMemory - bytes)
            {
                var oldest = recent.Last!;
                Flush(oldest.Value);
                pages.Remove(oldest.Value.Address);
                recent.RemoveLast();
                evictions++;
            }
            var reservation = new PageMemoryReservation(this, bytes);
            reservedMemory += bytes;
            peak = Math.Max(peak, pages.Count * (long)pageSize + reservedMemory);
            return reservation;
        }
    }

    internal void ReleaseMemory(long bytes)
    {
        lock (gate)
        {
            reservedMemory -= bytes;
        }
    }

    internal PageMemoryReservation? TryReserveScratchMemory(long bytes)
    {
        lock (gate)
        {
            // Keep paging useful when several typed lists grow concurrently.
            return bytes <= capacity / 4 * 3 - reservedMemory ? TryReserveMemory(bytes) : null;
        }
    }

    public int OpenHandleCount
    {
        get
        {
            lock (gate)
            {
                return handles.Count;
            }
        }
    }

    public long HandleOpenCount
    {
        get
        {
            lock (gate)
            {
                return handleOpens;
            }
        }
    }

    // Only used while the cache lock is held: a handle cannot be evicted during
    // a read, write or resize. Hundreds of open artifacts still use at most 32.
    internal SafeFileHandle Handle(PageFile file)
    {
        if (!Monitor.IsEntered(gate))
        {
            throw new InvalidOperationException("Paged file handles require the cache lock.");
        }
        ObjectDisposedException.ThrowIf(disposed, this);
        if (handles.TryGetValue(file, out var existing))
        {
            recentHandles.Remove(existing);
            recentHandles.AddFirst(existing);
            return existing.Value.Handle;
        }
        if (handles.Count == 32)
        {
            var oldest = recentHandles.Last!;
            oldest.Value.Handle.Dispose();
            handles.Remove(oldest.Value.File);
            recentHandles.RemoveLast();
        }
        var opened = file.Open();
        handles.Add(file, recentHandles.AddFirst(new CachedFileHandle(file, opened)));
        handleOpens++;
        return opened;
    }

    internal void Grow(PageFile file, long length)
    {
        lock (gate)
        {
            GrowLocked(file, length);
        }
    }

    private void GrowLocked(PageFile file, long length)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (length <= file.Length)
        {
            return;
        }
        length = checked((length + pageSize - 1) / pageSize * pageSize);
        var growth = length - file.Length;
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

    internal void ReserveSequentialScratch(long bytes)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (diskLimit != 0 && bytes > diskLimit - scratch)
            {
                throw new EchoException("Processing scratch-disk budget exceeded. Saved results are safe. Increase ProcessingDiskBudgetMb and resume.", 5);
            }
            scratch = checked(scratch + bytes);
        }
    }

    internal void ReleaseSequentialScratch(long bytes)
    {
        lock (gate)
        {
            scratch -= bytes;
        }
    }

    internal void RecordSequentialIo(long readBytes, long writtenBytes)
    {
        lock (gate)
        {
            reads += readBytes;
            writes += writtenBytes;
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
            GrowLocked(file, checked(offset + source.Length));
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

    internal void Release(PageFile file, bool discard = false)
    {
        lock (gate)
        {
            var node = recent.First;
            while (node != null)
            {
                var next = node.Next;
                if (node.Value.Address.File == file)
                {
                    if (!discard)
                    {
                        Flush(node.Value);
                    }
                    pages.Remove(node.Value.Address);
                    recent.Remove(node);
                }
                node = next;
            }
            if (handles.Remove(file, out var handle))
            {
                recentHandles.Remove(handle);
                handle.Value.Handle.Dispose();
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
            try
            {
                foreach (var page in recent)
                {
                    Flush(page);
                }
            }
            finally
            {
                recent.Clear();
                pages.Clear();
                foreach (var handle in recentHandles)
                {
                    handle.Handle.Dispose();
                }
                recentHandles.Clear();
                handles.Clear();
                disposed = true;
            }
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
        if ((pages.Count + 1L) * pageSize > capacity - reservedMemory)
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
        peak = Math.Max(peak, pages.Count * (long)pageSize + reservedMemory);
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
