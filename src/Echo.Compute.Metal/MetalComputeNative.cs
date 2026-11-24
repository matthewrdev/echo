using System.Runtime.InteropServices;
using System.Text;

namespace Echo.Compute.Metal;

internal static unsafe class MetalComputeNative
{
    [DllImport("EchoComputeMetal", EntryPoint = "echo_compute_create")]
    internal static extern IntPtr Create([MarshalAs(UnmanagedType.LPUTF8Str)] string source, StringBuilder details, int capacity);

    [DllImport("EchoComputeMetal", EntryPoint = "echo_compute_destroy")]
    internal static extern void Destroy(IntPtr handle);

    [DllImport("EchoComputeMetal", EntryPoint = "echo_compute_open")]
    internal static extern IntPtr Open(MetalComputeHandle device, long memoryBudgetBytes);

    [DllImport("EchoComputeMetal", EntryPoint = "echo_compute_close")]
    internal static extern void Close(IntPtr handle);

    [DllImport("EchoComputeMetal", EntryPoint = "echo_compute_buffer")]
    internal static extern int Buffer(IntPtr session, int slot, void* input, int bytes, StringBuilder error, int capacity);

    [DllImport("EchoComputeMetal", EntryPoint = "echo_compute_descriptor")]
    internal static extern int Descriptor(IntPtr session, int slot, ulong key, void* input, int bytes,
        out int reused, StringBuilder error, int capacity);

    [DllImport("EchoComputeMetal", EntryPoint = "echo_compute_write")]
    internal static extern int Write(IntPtr session, int slot, int offset, void* input, int bytes, StringBuilder error, int capacity);

    [DllImport("EchoComputeMetal", EntryPoint = "echo_compute_dispatch")]
    internal static extern int Dispatch(IntPtr session, [MarshalAs(UnmanagedType.LPUTF8Str)] string kernel, int count,
        int* parameters, int parameterCount, StringBuilder error, int capacity);

    [DllImport("EchoComputeMetal", EntryPoint = "echo_compute_dispatch_bound")]
    internal static extern int DispatchBound(IntPtr session, [MarshalAs(UnmanagedType.LPUTF8Str)] string kernel, int count,
        int* parameters, int parameterCount, int* slots, int slotCount, StringBuilder error, int capacity);

    [DllImport("EchoComputeMetal", EntryPoint = "echo_compute_submit")]
    internal static extern int Submit(IntPtr session, StringBuilder error, int capacity);

    [DllImport("EchoComputeMetal", EntryPoint = "echo_compute_read")]
    internal static extern int Read(IntPtr session, int slot, void* output, int bytes, StringBuilder error, int capacity);
    [DllImport("EchoComputeMetal", EntryPoint = "echo_compute_gpu_time")]
    internal static extern double GpuTime(IntPtr handle);

    [DllImport("EchoComputeMetal", EntryPoint = "echo_compute_close_timed")]
    internal static extern double CloseTimed(IntPtr handle);

}
