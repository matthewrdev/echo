using Echo.Storage.Pages;
namespace Echo.Storage.Resources;

internal sealed class ResidentScratchBlock : IDisposable
{
    private PageCache? cache;
    private readonly PageMemoryReservation reservation;

    public ResidentScratchBlock(PageCache cache, byte[] bytes, int length, PageMemoryReservation reservation)
    {
        this.cache = cache;
        this.reservation = reservation;
        Bytes = bytes;
        Length = length;
    }

    public byte[] Bytes
    {
        get;
    }

    public int Length
    {
        get;
    }

    public void Dispose() => Interlocked.Exchange(ref cache, null)?.ReturnScratchBlock(Bytes, reservation);
}
