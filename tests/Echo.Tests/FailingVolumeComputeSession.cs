using Echo.Compute;

namespace Echo.Tests;

internal sealed class FailingVolumeComputeSession : IComputeSession
{
    private int attempts;
    public string Device => "Injected volume failure";
    public int Attempts => attempts;

    public void Upload<T>(int slot, ReadOnlySpan<T> values) where T : unmanaged
    {
        Interlocked.Increment(ref attempts);
        throw new ComputeException("Injected volume failure");
    }

    public void UploadRange<T>(int slot, int elementOffset, ReadOnlySpan<T> values) where T : unmanaged => throw new InvalidOperationException();
    public void Allocate(int slot, int byteCount) => throw new InvalidOperationException();
    public void Dispatch(ComputeKernel kernel, int count, ReadOnlySpan<int> parameters) => throw new InvalidOperationException();
    public void Read<T>(int slot, Span<T> destination) where T : unmanaged => throw new InvalidOperationException();
    public void Dispose()
    {
    }
}
