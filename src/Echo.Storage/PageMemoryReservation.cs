namespace Echo.Storage;

public sealed class PageMemoryReservation : IDisposable
{
    private PageCache? cache;
    private readonly long bytes;

    internal PageMemoryReservation(PageCache cache, long bytes)
    {
        this.cache = cache;
        this.bytes = bytes;
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref cache, null)?.ReleaseMemory(bytes);
    }
}
