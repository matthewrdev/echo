using Echo.Compute;

namespace Echo.Tests;

internal sealed class FailingFusionComputeSession : IComputeSession
{
    private readonly IComputeSession inner;
    private int dispatches;

    public FailingFusionComputeSession(IComputeSession inner)
    {
        this.inner = inner;
    }

    public string Device => inner.Device;
    public void Upload<T>(int slot, ReadOnlySpan<T> values) where T : unmanaged => inner.Upload(slot, values);
    public void UploadRange<T>(int slot, int offset, ReadOnlySpan<T> values) where T : unmanaged => inner.UploadRange(slot, offset, values);
    public void Allocate(int slot, int byteCount) => inner.Allocate(slot, byteCount);
    public void Read<T>(int slot, Span<T> destination) where T : unmanaged => inner.Read(slot, destination);
    public void Dispose() => inner.Dispose();

    public void Dispatch(ComputeKernel kernel, int count, ReadOnlySpan<int> parameters)
    {
        if (++dispatches == 2)
        {
            throw new ComputeException("Injected failure after a partial fusion batch.");
        }

        inner.Dispatch(kernel, count, parameters);
    }
}
