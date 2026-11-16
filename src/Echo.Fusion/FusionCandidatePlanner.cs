using System.Runtime.ExceptionServices;
using Echo.Contracts;
using Echo.Geometry;
using Echo.Storage;

namespace Echo.Fusion;

internal static class FusionCandidatePlanner
{
    public static FusionCandidateGrid? Build(DenseCloudData cloud, DepthView?[] sources, double tolerance, double voxel,
        long limit, FusionGridPlanningOptions options, CancellationToken cancellation, WorkBuffers? work, FusionPlanningProgress status)
    {
        using var session = work == null ? new BulkDataSession(65536, 0) : null;
        using var records = work?.TemporaryRecords(new GridSpanRecordCodec())
            ?? session!.TemporaryRecords(Path.GetTempPath(), new GridSpanRecordCodec());
        var occupied = Coverage(cloud, sources, tolerance, voxel, limit, options, cancellation, records, status);
        if (occupied == null)
        {
            return null;
        }
        status.Start("Fusion extraction halo", occupied.Count * 4, "cell ranges");
        var runs = new FusionGridRuns(records, cancellation);
        var expandedX = runs.MergeSorted(Enumerable.Range(-1, 4).Select(x => Expand(occupied, x, 0, true, cancellation, status)));
        records.Delete(occupied);
        status.Start("Fusion extraction halo completion", expandedX.Count * 4, "cell ranges");
        var candidates = runs.MergeSorted(Enumerable.Range(-1, 4).Select(y => Expand(expandedX, 0, y, false, cancellation, status)));
        records.Delete(expandedX);
        return Materialize(candidates, limit, options.Axis, cancellation, work, status);
    }

    private static TemporaryRecordFile<GridSpan>? Coverage(DenseCloudData cloud, DepthView?[] sources,
        double tolerance, double voxel, long limit, FusionGridPlanningOptions options, CancellationToken cancellation,
        TemporaryRecordSet<GridSpan> records, FusionPlanningProgress status)
    {
        status.Start("Fusion candidate coverage", cloud.Samples.Count, "cloud points");
        var outputs = new TemporaryRecordFile<GridSpan>?[options.Workers];
        var exceeded = 0;
        try
        {
            Parallel.For(0, options.Workers, new ParallelOptions { CancellationToken = cancellation, MaxDegreeOfParallelism = options.Workers }, worker =>
            {
                var runs = new FusionGridRuns(records, cancellation);
                var batch = new FusionGridBatch(options.BatchCapacity, runs, options.Axis);
                var points = new DenseSample[4096];
                var start = (int)(cloud.Samples.Count * (long)worker / options.Workers);
                var end = (int)(cloud.Samples.Count * (long)(worker + 1) / options.Workers);
                while (start < end && Volatile.Read(ref exceeded) == 0)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var count = Math.Min(points.Length, end - start);
                    ReadPoints(cloud.Samples, start, points.AsSpan(0, count));
                    for (var index = 0; index < count; index++)
                    {
                        if (!FusionRayCoverage.Add(points[index], sources, tolerance, voxel, limit, batch, cancellation))
                        {
                            Interlocked.Exchange(ref exceeded, 1);
                            return;
                        }
                    }
                    start += count;
                    status.AdvanceBy(count);
                }
                if (Volatile.Read(ref exceeded) == 0)
                {
                    batch.Flush();
                    outputs[worker] = runs.Finish();
                }
            });
        }
        catch (AggregateException error)
        {
            ExceptionDispatchInfo.Capture(error.Flatten().InnerExceptions[0]).Throw();
            throw;
        }
        if (exceeded != 0)
        {
            return null;
        }
        status.Start("Fusion coverage merge", outputs.Sum(output => output!.Count), "cell ranges");
        var combined = new FusionGridRuns(records, cancellation);
        var result = combined.MergeSorted(outputs.Select(output => ReportRead(output!, cancellation, status)));
        foreach (var output in outputs)
        {
            records.Delete(output!);
        }
        return result;
    }

    private static IEnumerable<GridSpan> ReportRead(TemporaryRecordFile<GridSpan> source,
        CancellationToken cancellation, FusionPlanningProgress status)
    {
        var count = 0;
        foreach (var span in source.Read())
        {
            if (count == 0)
            {
                cancellation.ThrowIfCancellationRequested();
            }
            yield return span;
            count++;
            if (count == 4096)
            {
                status.AdvanceBy(count);
                count = 0;
            }
        }
        status.AdvanceBy(count);
    }

    private static void ReadPoints(IList<DenseSample> source, int start, Span<DenseSample> destination)
    {
        if (source is PagedList<DenseSample> paged)
        {
            paged.ReadRange(start, destination);
        }
        else
        {
            for (var index = 0; index < destination.Length; index++)
            {
                destination[index] = source[start + index];
            }
        }
    }

    private static IEnumerable<GridSpan> Expand(TemporaryRecordFile<GridSpan> occupied, int x, int y, bool expandZ,
        CancellationToken cancellation, FusionPlanningProgress status)
    {
        var completed = 0;
        // Dilation is separable: merge four shifts per axis, deduplicating
        // between passes. The first pass also extends the inclusive Z interval.
        foreach (var span in occupied.Read())
        {
            if (completed % 4096 == 0)
            {
                cancellation.ThrowIfCancellationRequested();
            }
            yield return new GridSpan(span.X + x, span.Y + y, span.FirstZ - (expandZ ? 1 : 0), span.LastZ + (expandZ ? 2 : 0));
            completed++;
            if (completed == 4096)
            {
                status.AdvanceBy(completed);
                completed = 0;
            }
        }
        status.AdvanceBy(completed);
    }

    private static FusionCandidateGrid? Materialize(TemporaryRecordFile<GridSpan> candidates, long limit, GridAxis axis,
        CancellationToken cancellation, WorkBuffers? work, FusionPlanningProgress status)
    {
        long count = 0;
        status.Start("Fusion grid storage", candidates.Count, "cell ranges");
        IList<GridSpan> result = work?.List(new GridSpanRecordCodec()) ?? new List<GridSpan>();
        var batch = new GridSpan[4096];
        var size = 0;
        foreach (var span in ReportRead(candidates, cancellation, status))
        {
            count = checked(count + span.CellCount);
            if (count > limit)
            {
                return null;
            }
            batch[size++] = span;
            if (size == batch.Length)
            {
                Append(result, batch);
                size = 0;
            }
        }
        cancellation.ThrowIfCancellationRequested();
        Append(result, batch.AsSpan(0, size));
        return new FusionCandidateGrid(result, count, axis);
    }

    private static void Append(IList<GridSpan> output, ReadOnlySpan<GridSpan> values)
    {
        if (output is PagedList<GridSpan> paged)
        {
            paged.AddRange(values);
        }
        else
        {
            foreach (var value in values)
            {
                output.Add(value);
            }
        }
    }
}
