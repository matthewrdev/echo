using System.Text;
using Echo.Telemetry;

namespace Echo.Compute.Metal;

internal sealed unsafe class MetalComputeSession : IComputeStreamingSession, IGpuTimingSource
{
    private readonly SemaphoreSlim gate;
    private readonly CancellationToken cancellation;
    private readonly StringBuilder error = new(4096);
    private IntPtr handle;
    private double completedGpu;
    public double CompletedGpuMilliseconds => handle == IntPtr.Zero ? completedGpu : MetalComputeNative.GpuTime(handle);

    public MetalComputeSession(IntPtr handle, string device, SemaphoreSlim gate, CancellationToken cancellation)
    {
        this.handle = handle;
        this.gate = gate;
        this.cancellation = cancellation;
        Device = device;
    }

    public string Device
    {
        get;
    }

    public void Upload<T>(int slot, ReadOnlySpan<T> values) where T : unmanaged
    {
        Check();
        fixed (T* pointer = values)
        {
            Verify(MetalComputeNative.Buffer(handle, slot, pointer, checked(values.Length * sizeof(T)), error, error.Capacity));
        }
    }

    public void UploadRange<T>(int slot, int elementOffset, ReadOnlySpan<T> values) where T : unmanaged
    {
        Check();
        fixed (T* pointer = values)
        {
            Verify(MetalComputeNative.Write(handle, slot, checked(elementOffset * sizeof(T)), pointer,
                checked(values.Length * sizeof(T)), error, error.Capacity));
        }
    }

    public void Allocate(int slot, int byteCount)
    {
        Check();
        Verify(MetalComputeNative.Buffer(handle, slot, null, byteCount, error, error.Capacity));
    }

    public void Dispatch(ComputeKernel kernel, int count, ReadOnlySpan<int> parameters)
    {
        Check();
        fixed (int* pointer = parameters)
        {
            Verify(MetalComputeNative.Dispatch(handle, MetalComputeKernel.Name(kernel), count, pointer, parameters.Length, error, error.Capacity));
        }
    }

    public void Read<T>(int slot, Span<T> destination) where T : unmanaged
    {
        Check();
        fixed (T* pointer = destination)
        {
            Verify(MetalComputeNative.Read(handle, slot, pointer, checked(destination.Length * sizeof(T)), error, error.Capacity));
        }
        cancellation.ThrowIfCancellationRequested();
    }

    public bool SupportsStreaming => true;

    public void DispatchBound(ComputeKernel kernel, int count, ReadOnlySpan<int> parameters, ReadOnlySpan<int> slots)
    {
        Check();
        fixed (int* parameterPointer = parameters)
        fixed (int* slotPointer = slots)
        {
            Verify(MetalComputeNative.DispatchBound(handle, MetalComputeKernel.Name(kernel), count,
                parameterPointer, parameters.Length, slotPointer, slots.Length, error, error.Capacity));
        }
    }

    public void Submit()
    {
        Check();
        Verify(MetalComputeNative.Submit(handle, error, error.Capacity));
    }

    private void Check()
    {
        ObjectDisposedException.ThrowIf(handle == IntPtr.Zero, this);
        cancellation.ThrowIfCancellationRequested();
    }

    private void Verify(int status)
    {
        if (status != 0)
        {
            throw new ComputeException(error.ToString());
        }
    }

    public void Dispose()
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }
        // Closing drains commands even when the cancellation token is set.
        completedGpu = MetalComputeNative.CloseTimed(handle);
        handle = IntPtr.Zero;
        gate.Release();
    }
}
