using System.Diagnostics;
using Echo.Contracts;

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
    private readonly Dictionary<PerformanceStatisticKey, PerformanceStatistic> statistics = [];
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
            Status = PerformanceStatus.Running
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
            span.DurationMilliseconds = Math.Max(0, clock.Elapsed.TotalMilliseconds - span.StartMilliseconds);
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
            if (!statistics.TryGetValue(key, out var statistic))
            {
                statistic = new PerformanceStatistic { Stage = span.Stage, Name = span.Name, Kind = span.Kind, Backend = span.Backend };
                statistics.Add(key, statistic);
            }
            statistic.Count++;
            statistic.ReusedCount += status == PerformanceStatus.Reused ? 1 : 0;
            statistic.FailedCount += status is PerformanceStatus.Failed or PerformanceStatus.Cancelled or PerformanceStatus.Interrupted ? 1 : 0;
            statistic.TotalMilliseconds += span.DurationMilliseconds;
            statistic.MaximumMilliseconds = Math.Max(statistic.MaximumMilliseconds, span.DurationMilliseconds);
            statistic.Items += span.Items ?? 0;
            statistic.Bytes += span.Bytes;
            if (span.GpuMilliseconds.HasValue)
            {
                statistic.GpuMilliseconds = (statistic.GpuMilliseconds ?? 0) + span.GpuMilliseconds.Value;
            }
        }
    }

    internal void Measure(PerformanceSpan span, long? items, long bytes, double? gpuMilliseconds)
    {
        lock (gate)
        {
            if (span.Status != PerformanceStatus.Running)
            {
                var statistic = statistics[new PerformanceStatisticKey(span.Stage, span.Name, span.Kind, span.Backend)];
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
                    Statistics = statistics.Values.Select(value => value with { }).ToList()
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
                Statistics = statistics.Values.Select(value => value with { }).ToList()
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
