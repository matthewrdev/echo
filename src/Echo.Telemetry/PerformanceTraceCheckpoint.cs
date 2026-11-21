using Echo.Models;

namespace Echo.Telemetry;

public sealed class PerformanceTraceCheckpoint
{
    public required PerformanceTraceDocument Document
    {
        get; init;
    }
    public int CompletedCount
    {
        get; init;
    }
}
