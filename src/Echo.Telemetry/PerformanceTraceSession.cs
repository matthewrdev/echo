using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using Echo.Models;

namespace Echo.Telemetry;

public sealed class PerformanceTraceSession : IDisposable
{
    private readonly object gate = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Process process = Process.GetCurrentProcess();
    private readonly double initialCpu;
    private readonly Action restore;
    private readonly int capacity;
    private readonly Dictionary<long, PerformanceTraceScope> active = [];
    private readonly List<PerformanceSpan> completed = [];
    private readonly List<PerformanceSpan> pending = [];
    private readonly HashSet<long> retained = [];
    private readonly Dictionary<string, int> detailCounts = [];
    private readonly Dictionary<PerformanceStatisticKey, PerformanceStatisticAccumulator> statistics = [];
    private readonly Dictionary<PerformanceMeasurementKey, PerformanceMeasurement> measurements = [];
    private readonly PerformanceTraceSummary summary;
    private long nextId;
    private bool disposed;

    public PerformanceTraceSession(string runId, string target, Action restore, int capacity = 2000)
    {
        this.restore = restore;
        this.capacity = Math.Max(1, capacity);
        initialCpu = process.TotalProcessorTime.TotalMilliseconds;
        summary = new PerformanceTraceSummary
        {
            Id = Guid.NewGuid().ToString("N"),
            RunId = runId,
            Target = target,
            StartedAt = DateTimeOffset.UtcNow,
            Status = PerformanceStatus.Running,
            Environment = new PerformanceTraceEnvironment(RuntimeInformation.FrameworkDescription,
                RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture.ToString(),
                Environment.ProcessorCount, GCSettings.IsServerGC)
        };
    }

    internal object Gate => gate;

    internal PerformanceTraceScope Begin(string name, PerformanceSpanKind kind, PerformanceBackend backend,
        string stage, PerformanceTraceScope? parent, Action restoreScope)
    {
        lock (gate)
        {
            var span = new PerformanceSpan
            {
                Id = ++nextId,
                ParentId = parent?.Span.Id,
                Name = name,
                Kind = kind,
                Backend = backend,
                Stage = stage,
                ThreadId = Environment.CurrentManagedThreadId,
                StartMilliseconds = clock.Elapsed.TotalMilliseconds
            };
            var scope = new PerformanceTraceScope(this, span, restoreScope);
            active.Add(span.Id, scope);
            var key = new PerformanceStatisticKey(stage, name, kind, backend);
            if (!statistics.TryGetValue(key, out var accumulator))
            {
                accumulator = new PerformanceStatisticAccumulator(key);
                statistics.Add(key, accumulator);
            }

            accumulator.Begin(span.StartMilliseconds);
            return scope;
        }
    }

    internal void End(PerformanceSpan span, PerformanceStatus status)
    {
        lock (gate)
        {
            if (!active.Remove(span.Id))
            {
                return;
            }

            var now = clock.Elapsed.TotalMilliseconds;
            span.DurationMilliseconds = Math.Max(0, now - span.StartMilliseconds);
            span.Status = status;
            // Preserve every operation/checkpoint and budget dispatch detail separately
            // for each stage, so feature extraction cannot consume the entire timeline.
            var detailed = span.Kind is PerformanceSpanKind.Compute or PerformanceSpanKind.Transfer or PerformanceSpanKind.Wait;
            var detailCount = detailCounts.GetValueOrDefault(span.Stage);
            if (!detailed || detailCount < capacity)
            {
                completed.Add(span);
                retained.Add(span.Id);
                pending.Add(span with
                {
                });
                if (detailed)
                {
                    detailCounts[span.Stage] = detailCount + 1;
                }
            }
            else
            {
                summary.DroppedSpans++;
            }

            var key = new PerformanceStatisticKey(span.Stage, span.Name, span.Kind, span.Backend);
            var accumulator = statistics[key];
            accumulator.End(span, now);
        }
    }

    internal void Measure(PerformanceSpan span, long? items, long bytes, double? gpuMilliseconds)
    {
        lock (gate)
        {
            if (span.Status != PerformanceStatus.Running)
            {
                var statistic = statistics[new PerformanceStatisticKey(span.Stage, span.Name, span.Kind, span.Backend)].Statistic;
                statistic.Items += (items ?? 0) - (span.Items ?? 0);
                statistic.Bytes += bytes - span.Bytes;
                if (gpuMilliseconds.HasValue)
                {
                    statistic.GpuMilliseconds = (statistic.GpuMilliseconds ?? 0) + gpuMilliseconds.Value - (span.GpuMilliseconds ?? 0);
                }
            }

            span.Items = items;
            span.Bytes = bytes;
            span.GpuMilliseconds = gpuMilliseconds;
            if (span.Status != PerformanceStatus.Running && retained.Contains(span.Id))
            {
                // A failed GPU call can report its final counters after closing.
                pending.Add(span with
                {
                });
            }
        }
    }

    internal PerformanceResourceSample? CaptureResources(PerformanceSpanKind kind)
        => kind is PerformanceSpanKind.Stage or PerformanceSpanKind.Operation or PerformanceSpanKind.Checkpoint
            ? new PerformanceResourceSample(kind == PerformanceSpanKind.Stage, process) : null;

    internal void CompleteResources(PerformanceResourceSample? resources, PerformanceSpan span) => resources?.Complete(span, process);

    internal void Measurement(string stage, string name, double value, string unit)
    {
        if (!double.IsFinite(value))
        {
            return;
        }

        lock (gate)
        {
            if (summary.Status != PerformanceStatus.Running)
            {
                return;
            }

            var key = new PerformanceMeasurementKey(stage, name, unit);
            if (!measurements.TryGetValue(key, out var measurement))
            {
                if (measurements.Count >= 256)
                {
                    summary.DroppedMeasurements++;
                    return;
                }

                measurement = new PerformanceMeasurement { Stage = stage, Name = name, Unit = unit, Minimum = value, Maximum = value };
                measurements.Add(key, measurement);
            }

            measurement.Count++;
            measurement.Total += value;
            measurement.Minimum = Math.Min(measurement.Minimum, value);
            measurement.Maximum = Math.Max(measurement.Maximum, value);
        }
    }

    public PerformanceTraceCheckpoint CaptureCheckpoint()
    {
        lock (gate)
        {
            if (summary.Status == PerformanceStatus.Running)
            {
                UpdateSummary();
            }

            return new PerformanceTraceCheckpoint
            {
                CompletedCount = pending.Count,
                Document = new PerformanceTraceDocument
                {
                    Summary = summary with { },
                    Spans = pending.Select(span => span with { }).Concat(active.Values.Select(scope => scope.Span with
                    {
                        DurationMilliseconds = Math.Max(0, clock.Elapsed.TotalMilliseconds - scope.Span.StartMilliseconds)
                    })).ToList(),
                    Statistics = statistics.Values.Select(value => value.Snapshot(clock.Elapsed.TotalMilliseconds)).ToList(),
                    Measurements = measurements.Values.Select(value => value with { }).ToList()
                }
            };
        }
    }

    public void Acknowledge(PerformanceTraceCheckpoint checkpoint)
    {
        lock (gate)
        {
            pending.RemoveRange(0, checkpoint.CompletedCount);
        }
    }

    public PerformanceTraceDocument Snapshot()
    {
        lock (gate)
        {
            if (summary.Status == PerformanceStatus.Running)
            {
                UpdateSummary();
            }

            return new PerformanceTraceDocument
            {
                Summary = summary with { },
                Spans = completed.Select(span => span with { }).Concat(active.Values.Select(scope => scope.Span with
                {
                    DurationMilliseconds = Math.Max(0, clock.Elapsed.TotalMilliseconds - scope.Span.StartMilliseconds)
                })).OrderBy(span => span.Id).ToList(),
                Statistics = statistics.Values.Select(value => value.Snapshot(clock.Elapsed.TotalMilliseconds)).ToList(),
                Measurements = measurements.Values.Select(value => value with { }).ToList()
            };
        }
    }

    public void Finish(PerformanceStatus status)
    {
        lock (gate)
        {
            if (summary.Status != PerformanceStatus.Running)
            {
                return;
            }

            foreach (var scope in active.Values.ToArray())
            {
                scope.Complete(status == PerformanceStatus.Completed ? PerformanceStatus.Interrupted : status);
            }

            UpdateSummary();
            summary.Status = status;
            clock.Stop();
        }
    }

    private void UpdateSummary()
    {
        process.Refresh();
        summary.CpuMilliseconds = Math.Max(0, process.TotalProcessorTime.TotalMilliseconds - initialCpu);
        summary.DurationMilliseconds = clock.Elapsed.TotalMilliseconds;
        summary.UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        Finish(PerformanceStatus.Interrupted);
        disposed = true;
        process.Dispose();
        restore();
    }
}
