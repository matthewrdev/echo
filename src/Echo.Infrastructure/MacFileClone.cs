using System.Runtime.InteropServices;

namespace Echo.Infrastructure;

internal static class MacFileClone
{
    [DllImport("libSystem.B.dylib", EntryPoint = "clonefile")]
    internal static extern int Copy([MarshalAs(UnmanagedType.LPUTF8Str)] string source,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string destination, int flags);
}
