using Echo.Models;

namespace Echo.Telemetry;

public sealed class ProcessCounters
{
    public int ProcessId
    {
        get; init;
    }
    public TimeSpan CpuTime
    {
        get; init;
    }
    public List<ThreadProgress>? Threads
    {
        get; init;
    }
}
