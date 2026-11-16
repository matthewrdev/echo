using System.Runtime.InteropServices;
using System.Text;

namespace Echo.Texturing.Metal;

internal static class MetalTextureNative
{
    [DllImport("EchoTextureMetal", EntryPoint = "echo_texture_metal_create", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr Create([MarshalAs(UnmanagedType.LPUTF8Str)] string source, int size,
        [Out] StringBuilder details, int capacity);

    [DllImport("EchoTextureMetal", EntryPoint = "echo_texture_metal_find", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Find(MetalTextureHandle session, uint[] occupancy, uint[] masks,
        uint[] spans, int spanCount, int maximumBottom, out uint result, [Out] StringBuilder details, int capacity);

    [DllImport("EchoTextureMetal", EntryPoint = "echo_texture_metal_destroy", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Destroy(IntPtr session);
}
