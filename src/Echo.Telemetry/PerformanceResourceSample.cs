using System.Diagnostics;
using Echo.Contracts;

namespace Echo.Telemetry;

internal sealed class PerformanceResourceSample
{
    private readonly int threadId = Environment.CurrentManagedThreadId;
    private readonly long threadAllocated = GC.GetAllocatedBytesForCurrentThread();
    private readonly long processAllocated;
    private readonly double processCpu;
    private readonly double pause;
    private readonly int gen0;
    private readonly int gen1;
    private readonly int gen2;

    public PerformanceResourceSample(bool stage, Process process)
    {
        if (stage)
        {
            processAllocated = GC.GetTotalAllocatedBytes(precise: false);
            processCpu = process.TotalProcessorTime.TotalMilliseconds;
            pause = GC.GetTotalPauseDuration().TotalMilliseconds;
            gen0 = GC.CollectionCount(0);
            gen1 = GC.CollectionCount(1);
            gen2 = GC.CollectionCount(2);
        }
    }

    public void Complete(PerformanceSpan span, Process process)
    {
        if (threadId == Environment.CurrentManagedThreadId)
        {
            span.ThreadAllocatedBytes = Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - threadAllocated);
        }
        if (span.Kind == PerformanceSpanKind.Stage)
        {
            span.ProcessAllocatedBytes = Math.Max(0, GC.GetTotalAllocatedBytes(precise: false) - processAllocated);
            span.ProcessCpuMilliseconds = Math.Max(0, process.TotalProcessorTime.TotalMilliseconds - processCpu);
            span.GcPauseMilliseconds = Math.Max(0, GC.GetTotalPauseDuration().TotalMilliseconds - pause);
            span.Gen0Collections = GC.CollectionCount(0) - gen0;
            span.Gen1Collections = GC.CollectionCount(1) - gen1;
            span.Gen2Collections = GC.CollectionCount(2) - gen2;
        }
    }
}
