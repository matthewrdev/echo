using System.Buffers;
using System.Runtime.InteropServices;
using Echo.Models;
using Echo.Storage.Records;
using Echo.Storage.Resources;

namespace Echo.Fusion;

internal sealed class FusionIntervalLabeller
{
    private readonly List<FusionVoxelRun> runs = [];
    private int[] order = [];
    private int[] parents = [];

    public FusionComponentLabels? TryBuild(IList<VolumeSample> samples, CancellationToken cancellation,
        WorkBuffers? work, Action<string>? progress)
    {
        var maximumRuns = Math.Min(2_000_000, samples.Count / 4);
        using var reservation = work?.TryReserveMemory((long)maximumRuns * 100);
        if (maximumRuns == 0 || (work != null && reservation == null))
        {
            return null;
        }

        progress?.Invoke("Grouping adjacent fusion samples into row intervals.");
        if (!ReadRuns(samples, maximumRuns, cancellation))
        {
            return null;
        }

        order = Enumerable.Range(0, runs.Count).ToArray();
        parents = (int[])order.Clone();
        Array.Sort(order, CompareRuns);
        var rows = BuildRows(cancellation);
        if (rows == null)
        {
            // The original index reports duplicate samples with its usual error.
            return null;
        }

        var allRows = CollectionsMarshal.AsSpan(rows);
        foreach (var row in allRows)
        {
            cancellation.ThrowIfCancellationRequested();
            Connect(allRows, row, (long)row.Y - 1, row.Z);
            for (var offset = -1; offset <= 1; offset++)
            {
                Connect(allRows, row, (long)row.Y + offset, (long)row.Z - 1);
            }
        }

        return ExpandLabels(samples.Count, work, cancellation);
    }

    private bool ReadRuns(IList<VolumeSample> samples, int maximumRuns, CancellationToken cancellation)
    {
        var buffer = ArrayPool<VolumeSample>.Shared.Rent(4096);
        try
        {
            FusionVoxelRun run = default;
            for (var start = 0; start < samples.Count;)
            {
                cancellation.ThrowIfCancellationRequested();
                var block = buffer.AsSpan(0, Math.Min(buffer.Length, samples.Count - start));
                RecordListOperations.Read(samples, start, block);
                for (var index = 0; index < block.Length; index++)
                {
                    ref readonly var sample = ref block[index];
                    // Preserve the original lookup's wraparound semantics at the Int32 endpoints.
                    if (AtCoordinateLimit(sample.X) || AtCoordinateLimit(sample.Y) || AtCoordinateLimit(sample.Z))
                    {
                        return false;
                    }

                    var extendsRun = run.Count > 0 && sample.Y == run.Y
                        && sample.Z == run.Z && sample.X == run.End + 1;
                    if (extendsRun)
                    {
                        run = run with
                        {
                            Count = run.Count + 1
                        };
                        continue;
                    }

                    if (run.Count > 0)
                    {
                        runs.Add(run);
                        if (runs.Count == maximumRuns)
                        {
                            return false;
                        }
                    }

                    run = new FusionVoxelRun(sample.X, sample.Y, sample.Z, start + index, 1);
                }

                start += block.Length;
            }

            runs.Add(run);
            return true;
        }
        finally
        {
            ArrayPool<VolumeSample>.Shared.Return(buffer);
        }
    }

    private bool AtCoordinateLimit(int coordinate) => coordinate is int.MinValue or int.MaxValue;

    private int CompareRuns(int left, int right)
    {
        var a = runs[left];
        var b = runs[right];
        var byZ = a.Z.CompareTo(b.Z);
        if (byZ != 0)
        {
            return byZ;
        }

        var byY = a.Y.CompareTo(b.Y);
        return byY != 0 ? byY : a.X.CompareTo(b.X);
    }

    private List<FusionVoxelRow>? BuildRows(CancellationToken cancellation)
    {
        var rows = new List<FusionVoxelRow>();
        for (var cursor = 0; cursor < order.Length;)
        {
            cancellation.ThrowIfCancellationRequested();
            var first = cursor++;
            var run = runs[order[first]];
            while (cursor < order.Length && runs[order[cursor]].Y == run.Y && runs[order[cursor]].Z == run.Z)
            {
                var previous = runs[order[cursor - 1]];
                var next = runs[order[cursor]];
                if (previous.End >= next.X)
                {
                    return null;
                }

                if (previous.End + 1 == next.X)
                {
                    Join(order[cursor - 1], order[cursor]);
                }

                cursor++;
            }

            rows.Add(new FusionVoxelRow(run.Y, run.Z, first, cursor));
        }

        return rows;
    }

    private void Connect(ReadOnlySpan<FusionVoxelRow> rows, FusionVoxelRow left, long y, long z)
    {
        var lower = 0;
        var upper = rows.Length;
        while (lower < upper)
        {
            var middle = lower + (upper - lower) / 2;
            var candidate = rows[middle];
            if (candidate.Z < z || (candidate.Z == z && candidate.Y < y))
            {
                lower = middle + 1;
            }
            else
            {
                upper = middle;
            }
        }

        if (lower == rows.Length || rows[lower].Y != y || rows[lower].Z != z)
        {
            return;
        }

        var right = rows[lower];
        var first = right.First;
        for (var index = left.First; index < left.End; index++)
        {
            var run = runs[order[index]];
            while (first < right.End && runs[order[first]].End + 1 < run.X)
            {
                first++;
            }

            for (var next = first; next < right.End && runs[order[next]].X <= run.End + 1; next++)
            {
                Join(order[index], order[next]);
            }
        }
    }

    private void Join(int left, int right)
    {
        var a = Root(left);
        var b = Root(right);
        if (a != b)
        {
            // Run order is input order, so the minimum root preserves the original seed and tie break.
            parents[Math.Max(a, b)] = Math.Min(a, b);
        }
    }

    private int Root(int index)
    {
        while (index != parents[index])
        {
            parents[index] = parents[parents[index]];
            index = parents[index];
        }

        return index;
    }

    private FusionComponentLabels ExpandLabels(int count, WorkBuffers? work, CancellationToken cancellation)
    {
        var counts = new int[runs.Count];
        IList<int> labels = work?.Filled(count, 0, new IntRecordCodec(), cancellation) ?? new int[count];
        IList<int> sizes = work?.Filled(count, 0, new IntRecordCodec(), cancellation) ?? new int[count];
        var buffer = ArrayPool<int>.Shared.Rent(4096);
        try
        {
            var filled = 0;
            var offset = 0;
            for (var index = 0; index < runs.Count; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                var root = Root(index);
                var length = runs[index].Count;
                counts[root] += length;
                while (length > 0)
                {
                    var copied = Math.Min(length, 4096 - filled);
                    buffer.AsSpan(filled, copied).Fill(runs[root].Start);
                    filled += copied;
                    length -= copied;
                    if (filled == 4096)
                    {
                        RecordListOperations.Write(labels, offset, buffer.AsSpan(0, filled));
                        offset += filled;
                        filled = 0;
                    }
                }
            }

            RecordListOperations.Write(labels, offset, buffer.AsSpan(0, filled));
        }
        finally
        {
            ArrayPool<int>.Shared.Return(buffer);
        }

        var largest = 0;
        var seeds = new List<int>();
        for (var index = 0; index < counts.Length; index++)
        {
            if (counts[index] == 0)
            {
                continue;
            }

            sizes[runs[index].Start] = counts[index];
            seeds.Add(runs[index].Start);
            if (counts[index] > counts[largest])
            {
                largest = index;
            }
        }

        return new FusionComponentLabels
        {
            Labels = labels,
            Sizes = sizes,
            Largest = runs[largest].Start,
            RegionCount = seeds.Count,
            RegionSeeds = seeds
        };
    }

}
