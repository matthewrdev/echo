using System.Runtime.CompilerServices;
using Echo.Compute;

namespace Echo.Benchmarks;

internal sealed class ProfileComputeSession : IComputeStreamingSession
{
    private readonly IComputeSession inner;
    private readonly ProfileComputeService profile;

    public ProfileComputeSession(IComputeSession inner, ProfileComputeService profile)
    {
        this.inner = inner;
        this.profile = profile;
    }

    public string Device => inner.Device;
    public bool SupportsStreaming => inner is IComputeStreamingSession { SupportsStreaming: true };
    public void Allocate(int slot, int bytes) => inner.Allocate(slot, bytes);

    public void Upload<T>(int slot, ReadOnlySpan<T> values) where T : unmanaged
    {
        inner.Upload(slot, values);
        profile.UploadedBytes += values.Length * (long)Unsafe.SizeOf<T>();
    }

    public void UploadRange<T>(int slot, int offset, ReadOnlySpan<T> values) where T : unmanaged
    {
        inner.UploadRange(slot, offset, values);
        profile.UploadedBytes += values.Length * (long)Unsafe.SizeOf<T>();
    }

    public void Read<T>(int slot, Span<T> values) where T : unmanaged
    {
        inner.Read(slot, values);
        profile.ReadbackBytes += values.Length * (long)Unsafe.SizeOf<T>();
    }

    public void Dispatch(ComputeKernel kernel, int count, ReadOnlySpan<int> parameters)
    {
        inner.Dispatch(kernel, count, parameters);
        profile.Dispatches++;
    }

    public void DispatchBound(ComputeKernel kernel, int count, ReadOnlySpan<int> parameters, ReadOnlySpan<int> slots)
    {
        ((IComputeStreamingSession)inner).DispatchBound(kernel, count, parameters, slots);
        profile.Dispatches++;
    }

    public void Submit() => ((IComputeStreamingSession)inner).Submit();
    public void Dispose() => inner.Dispose();
}
