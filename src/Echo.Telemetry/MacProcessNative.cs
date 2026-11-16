using System.Runtime.InteropServices;

namespace Echo.Telemetry;

internal static class MacProcessNative
{
    [DllImport("/usr/lib/libproc.dylib", EntryPoint = "proc_pidinfo")]
    internal static extern int ListThreads(int processId, int flavor, ulong argument, [Out] ulong[] buffer, int size);

    [DllImport("/usr/lib/libproc.dylib", EntryPoint = "proc_pidinfo")]
    internal static extern int ReadThread(int processId, int flavor, ulong thread, out MacThreadInfo info, int size);
}
