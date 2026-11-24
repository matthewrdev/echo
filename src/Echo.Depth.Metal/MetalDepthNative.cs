using System.Runtime.InteropServices;
using System.Text;

namespace Echo.Depth.Metal;

internal static class MetalDepthNative
{
    [DllImport("EchoDepthMetal", EntryPoint = "echo_metal_compile", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr Compile([MarshalAs(UnmanagedType.LPUTF8Str)] string source,
        [Out] StringBuilder details, int capacity);

    [DllImport("EchoDepthMetal", EntryPoint = "echo_metal_destroy_pipeline", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void DestroyPipeline(IntPtr pipeline);

    [DllImport("EchoDepthMetal", EntryPoint = "echo_metal_create_session", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr Create(MetalDepthPipelineHandle pipeline,
        float[] views, int viewCount, float[] gray, int grayCount, [Out] StringBuilder details, int capacity);

    [DllImport("EchoDepthMetal", EntryPoint = "echo_metal_calculate", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Calculate(MetalDepthHandle session, float[] jobs, int sampleCount,
        float[] depths, int planeCount, [Out] float[] output, [Out] StringBuilder error, int capacity);

    [DllImport("EchoDepthMetal", EntryPoint = "echo_metal_destroy", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Destroy(IntPtr session);
    [DllImport("EchoDepthMetal", EntryPoint = "echo_metal_gpu_time")]
    internal static extern double GpuTime(MetalDepthHandle handle);

}
