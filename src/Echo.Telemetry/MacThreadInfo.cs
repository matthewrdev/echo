using System.Runtime.InteropServices;

namespace Echo.Telemetry;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal struct MacThreadInfo
{
    public ulong userTime;
    public ulong systemTime;
    public int cpuUsage;
    public int policy;
    public int runState;
    public int flags;
    public int sleepTime;
    public int currentPriority;
    public int priority;
    public int maximumPriority;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string name;
}
