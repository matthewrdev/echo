using System.Numerics;
using Echo.Models;

namespace Echo.Telemetry;

internal sealed class PerformanceStatisticAccumulator
{
    private readonly long[] histogram = new long[33];
    private double busySince;
    private double busyMilliseconds;
    private int active;

    public PerformanceStatisticAccumulator(PerformanceStatisticKey key)
    {
        Statistic = new PerformanceStatistic
        {
            Stage = key.Stage,
            Name = key.Name,
            Kind = key.Kind,
            Backend = key.Backend,
            MaximumConcurrency = 0
        };
    }

    public PerformanceStatistic Statistic
    {
        get;
    }

    public void Begin(double now)
    {
        if (active++ == 0)
        {
            busySince = now;
        }
        Statistic.MaximumConcurrency = Math.Max(Statistic.MaximumConcurrency ?? 0, active);
    }

    public void End(PerformanceSpan span, double now)
    {
        if (--active == 0)
        {
            busyMilliseconds += now - busySince;
        }
        var micros = (ulong)Math.Clamp(Math.Ceiling(span.DurationMilliseconds * 1000), 1, uint.MaxValue);
        var bucket = micros <= 1 ? 0 : BitOperations.Log2(micros - 1) + 1;
        histogram[bucket]++;
        Statistic.Count++;
        Statistic.ReusedCount += span.Status == PerformanceStatus.Reused ? 1 : 0;
        Statistic.FailedCount += span.Status is PerformanceStatus.Failed or PerformanceStatus.Cancelled or PerformanceStatus.Interrupted ? 1 : 0;
        Statistic.TotalMilliseconds += span.DurationMilliseconds;
        Statistic.MaximumMilliseconds = Math.Max(Statistic.MaximumMilliseconds, span.DurationMilliseconds);
        Statistic.Items += span.Items ?? 0;
        Statistic.Bytes += span.Bytes;
        Statistic.GpuMilliseconds = Add(Statistic.GpuMilliseconds, span.GpuMilliseconds);
        Statistic.ThreadAllocatedBytes = Add(Statistic.ThreadAllocatedBytes, span.ThreadAllocatedBytes);
        Statistic.ThreadAllocationSamples += span.ThreadAllocatedBytes.HasValue ? 1 : 0;
        Statistic.ProcessAllocatedBytes = Add(Statistic.ProcessAllocatedBytes, span.ProcessAllocatedBytes);
        Statistic.ProcessCpuMilliseconds = Add(Statistic.ProcessCpuMilliseconds, span.ProcessCpuMilliseconds);
        Statistic.GcPauseMilliseconds = Add(Statistic.GcPauseMilliseconds, span.GcPauseMilliseconds);
        Statistic.Gen0Collections = Add(Statistic.Gen0Collections, span.Gen0Collections);
        Statistic.Gen1Collections = Add(Statistic.Gen1Collections, span.Gen1Collections);
        Statistic.Gen2Collections = Add(Statistic.Gen2Collections, span.Gen2Collections);
    }

    public PerformanceStatistic Snapshot(double now) => Statistic with
    {
        BusyMilliseconds = busyMilliseconds + (active > 0 ? now - busySince : 0),
        MedianUpperMilliseconds = Percentile(.5),
        P95UpperMilliseconds = Percentile(.95)
    };

    private double? Percentile(double percentile)
    {
        if (Statistic.Count == 0)
        {
            return null;
        }
        var rank = (long)Math.Ceiling(Statistic.Count * percentile);
        long count = 0;
        for (var index = 0; index < histogram.Length; index++)
        {
            count += histogram[index];
            if (count >= rank)
            {
                return index == histogram.Length - 1 ? Statistic.MaximumMilliseconds
                    : Math.Min(Statistic.MaximumMilliseconds, (1L << index) / 1000.0);
            }
        }
        return Statistic.MaximumMilliseconds;
    }

    private long? Add(long? total, long? value) => value.HasValue ? (total ?? 0) + value : total;
    private double? Add(double? total, double? value) => value.HasValue ? (total ?? 0) + value : total;
}
