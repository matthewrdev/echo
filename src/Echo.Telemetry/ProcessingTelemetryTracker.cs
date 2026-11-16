using System.Diagnostics;
using Echo.Contracts;

namespace Echo.Telemetry;

// Owned by one run, with no shared sampling baselines between runs.
public sealed class ProcessingTelemetryTracker
{
    private readonly IProcessTelemetryReader reader;
    private Dictionary<int, ProcessCounters> previous = [];
    private long sampledAt;
    private ProcessorProgress? latest;

    public ProcessingTelemetryTracker(IProcessTelemetryReader reader)
    {
        this.reader = reader;
    }

    public ProcessorProgress Sample(IReadOnlyList<int> processIds)
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(sampledAt, now).TotalSeconds;
        if (latest != null && elapsed < 1)
        {
            return latest;
        }

        var current = processIds.Select(reader.Read).OfType<ProcessCounters>().ToDictionary(value => value.ProcessId);
        var cpuSeconds = 0.0;
        var comparable = false;
        foreach (var value in current.Values)
        {
            if (previous.TryGetValue(value.ProcessId, out var before))
            {
                cpuSeconds += Math.Max(0, (value.CpuTime - before.CpuTime).TotalSeconds);
                comparable = true;
            }
        }

        var hasThreads = current.Count > 0 && current.Values.All(value => value.Threads != null);
        latest = new ProcessorProgress
        {
            CpuPercent = comparable && elapsed > 0 ? cpuSeconds / elapsed * 100 : null,
            LogicalProcessors = Environment.ProcessorCount,
            Threads = hasThreads ? current.Values.SelectMany(value => value.Threads!).ToList() : null
        };
        previous = current;
        sampledAt = now;
        return latest;
    }
}
