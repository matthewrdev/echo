using Echo.Models;
using Echo.Telemetry;

namespace Echo.Tests;

public sealed class PerformanceDiagnosticsTests
{
    private readonly IPerformanceTrace trace;

    public PerformanceDiagnosticsTests(IPerformanceTrace trace)
    {
        this.trace = trace;
    }

    internal void Run(TestSuite tests)
    {
        tests.Run("Performance aggregates distinguish overlapping work from covered wall time", () =>
        {
            var accumulator = new PerformanceStatisticAccumulator(new PerformanceStatisticKey("matching", "pair", PerformanceSpanKind.Operation, PerformanceBackend.Cpu));
            accumulator.Begin(0);
            accumulator.Begin(2);
            accumulator.End(new PerformanceSpan { DurationMilliseconds = 8, Status = PerformanceStatus.Completed }, 8);
            var live = accumulator.Snapshot(10);
            Check(live.BusyMilliseconds == 10 && live.Count == 1, "Live coverage includes the unfinished call");
            accumulator.End(new PerformanceSpan { DurationMilliseconds = 10, Status = PerformanceStatus.Completed }, 12);
            accumulator.Begin(20);
            accumulator.End(new PerformanceSpan { DurationMilliseconds = 4, Status = PerformanceStatus.Reused, Items = 3, Bytes = 64, GpuMilliseconds = 2 }, 24);
            var result = accumulator.Snapshot(30);
            Check(result.BusyMilliseconds == 16 && result.TotalMilliseconds == 22, "Coverage excludes idle gaps and counts overlap once");
            Check(result.MaximumConcurrency == 2 && result.Count == 3 && result.ReusedCount == 1, "Concurrency and status counts retained");
            Check(result.Items == 3 && result.Bytes == 64 && result.GpuMilliseconds == 2, "Work counters retained");
            Check(live.Count == 1 && live.BusyMilliseconds == 10, "Snapshots are independent");
        });
        tests.Run("Performance percentile bounds include outliers and overflow durations", () =>
        {
            var accumulator = new PerformanceStatisticAccumulator(new PerformanceStatisticKey("depth", "batch", PerformanceSpanKind.Compute, PerformanceBackend.Metal));
            for (var index = 0; index < 100; index++)
            {
                var duration = index == 99 ? 100 : 1;
                accumulator.Begin(index * 100);
                accumulator.End(new PerformanceSpan { DurationMilliseconds = duration }, index * 100 + duration);
            }

            var result = accumulator.Snapshot(10000);
            Check(result.MedianUpperMilliseconds == 1.024 && result.P95UpperMilliseconds == 1.024, "Percentiles use bounded duration buckets");
            Check(result.MaximumMilliseconds == 100 && result.Count == 100, "The outlier remains represented");
            var overflow = new PerformanceStatisticAccumulator(new PerformanceStatisticKey("mesh", "long", PerformanceSpanKind.Stage, PerformanceBackend.Cpu));
            overflow.Begin(0);
            overflow.End(new PerformanceSpan { DurationMilliseconds = 8_000_000 }, 8_000_000);
            Check(overflow.Snapshot(8_000_000).P95UpperMilliseconds == 8_000_000, "Overflow bucket must not understate its upper bound");
        });
        tests.Run("Performance allocations retain process totals and omit cross-thread samples", () =>
        {
            using var session = trace.BeginRun(Guid.NewGuid().ToString("N"), "matching");
            using (trace.Begin("matching", PerformanceSpanKind.Stage, stage: "matching"))
            {
                using (trace.Begin("allocating", PerformanceSpanKind.Operation))
                {
                    GC.KeepAlive(new byte[1_000_000]);
                }

                using var moved = trace.Begin("cross thread", PerformanceSpanKind.Operation);
                var worker = new Thread(() => moved!.Complete());
                worker.Start();
                worker.Join();
            }

            session.Finish(PerformanceStatus.Completed);
            var result = session.Snapshot();
            Check(result.Spans.Single(span => span.Name == "allocating").ThreadAllocatedBytes >= 1_000_000, "Calling-thread allocations measured");
            Check(result.Spans.Single(span => span.Name == "cross thread").ThreadAllocatedBytes == null, "Cross-thread completion cannot use an unrelated allocation counter");
            var stage = result.Statistics.Single(row => row.Kind == PerformanceSpanKind.Stage);
            Check(stage.ProcessAllocatedBytes >= 1_000_000 && stage.ProcessCpuMilliseconds >= 0 && stage.GcPauseMilliseconds >= 0, "Stage process resources recorded");
            Check(stage.Gen0Collections >= 0 && stage.Gen1Collections >= 0 && stage.Gen2Collections >= 0, "GC collection deltas recorded");
            Check(result.Summary.Environment?.LogicalProcessors > 0, "Runtime environment recorded");
        });
        tests.Run("Performance measurement memory is bounded and snapshots own their values", () =>
        {
            using var session = trace.BeginRun(Guid.NewGuid().ToString("N"), "depth");
            using (trace.Begin("depth", PerformanceSpanKind.Stage, stage: "depth"))
            {
                trace.Measurement("batch", 2, "samples");
                trace.Measurement("batch", 6, "samples");
                trace.Measurement("batch", double.NaN, "samples");
                trace.Measurement("invalid", double.PositiveInfinity);
                for (var index = 0; index < 300; index++)
                {
                    trace.Measurement("counter " + index, index);
                }

                var first = session.Snapshot();
                var sample = first.Measurements.Single(row => row.Name == "batch");
                Check(sample.Count == 2 && sample.Total == 8 && sample.Minimum == 2 && sample.Maximum == 6, "Measurement aggregates are accurate");
                sample.Total = -1;
                first.Measurements.Clear();
                var second = session.Snapshot();
                Check(second.Measurements.Count == 256 && second.Summary.DroppedMeasurements == 45, "Distinct measurements are capped");
                Check(second.Measurements.Single(row => row.Name == "batch").Total == 8, "Measurements are deeply copied");
                Check(second.Spans.Count == 1, "Measurements do not create timeline events");
            }
        });
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
