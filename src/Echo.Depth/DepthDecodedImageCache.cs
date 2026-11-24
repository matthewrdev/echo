using SkiaSharp;

namespace Echo.Depth;

public sealed class DepthDecodedImageCache : IDisposable
{
    private readonly object gate = new();
    private readonly long idleBudget;
    private readonly Dictionary<string, DepthDecodedImage> entries = new(StringComparer.Ordinal);
    private readonly LinkedList<DepthDecodedImage> idle = new();
    private long idleBytes;
    private bool disposed;

    public DepthDecodedImageCache(long idleBudget)
    {
        this.idleBudget = Math.Max(0, idleBudget);
    }

    internal SKBitmap Rent(string path, Func<SKBitmap> decode)
    {
        DepthDecodedImage entry;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!entries.TryGetValue(path, out entry!))
            {
                entry = new DepthDecodedImage(path, decode);
                entries.Add(path, entry);
            }

            if (entry.IdleNode.List != null)
            {
                idle.Remove(entry.IdleNode);
                idleBytes -= entry.Bytes;
            }

            entry.Users++;
        }

        try
        {
            return entry.Bitmap.Value;
        }
        catch
        {
            Return(path);
            throw;
        }
    }

    internal void Return(string path)
    {
        lock (gate)
        {
            var entry = entries[path];
            if (--entry.Users != 0)
            {
                return;
            }

            if (!entry.Bitmap.IsValueCreated)
            {
                entries.Remove(path);
                return;
            }

            if (disposed)
            {
                entries.Remove(path);
                entry.Bitmap.Value.Dispose();
                return;
            }

            idle.AddFirst(entry.IdleNode);
            idleBytes += entry.Bytes;
            while (idleBytes > idleBudget && idle.Last is { } oldest)
            {
                Evict(oldest);
            }
        }
    }

    private void Evict(LinkedListNode<DepthDecodedImage> node)
    {
        var entry = node.Value;
        idle.Remove(node);
        entries.Remove(entry.Path);
        idleBytes -= entry.Bytes;
        entry.Bitmap.Value.Dispose();
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            while (idle.Last is { } oldest)
            {
                Evict(oldest);
            }
        }
    }
}
