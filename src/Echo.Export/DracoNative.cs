using System.Runtime.InteropServices;

namespace Echo.Export;

internal static partial class DracoNative
{
    [LibraryImport("echo-draco", EntryPoint = "echo_draco_encode")]
    internal static unsafe partial int Encode(float* positions, float* uvs, uint* indices,
        int vertices, int triangles, int uvBits, out DracoMeshInfo result);

    [LibraryImport("echo-draco", EntryPoint = "echo_draco_free")]
    internal static partial void Free(nint data);
}
