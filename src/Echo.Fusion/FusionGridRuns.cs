using Echo.Storage;

namespace Echo.Fusion;

internal sealed class FusionGridRuns
{
    private const int FanIn = 8;
    private readonly TemporaryRecordSet<GridSpan> records;
    private readonly CancellationToken cancellation;
    private readonly List<List<TemporaryRecordFile<GridSpan>>> levels = [];

    public FusionGridRuns(TemporaryRecordSet<GridSpan> records, CancellationToken cancellation)
    {
        this.records = records;
        this.cancellation = cancellation;
    }

    public void Add(IEnumerable<GridSpan> sorted) => Add(records.Write(Coalesce(sorted)), 0);

    public void Add(TemporaryRecordFile<GridSpan> run) => Add(run, 0);

    public TemporaryRecordFile<GridSpan> Finish()
    {
        var pending = levels.SelectMany(level => level).ToList();
        levels.Clear();
        if (pending.Count == 0)
        {
            return records.Write([]);
        }
        while (pending.Count > 1)
        {
            var group = pending.Take(FanIn).ToArray();
            pending.RemoveRange(0, group.Length);
            pending.Add(Merge(group));
        }
        return pending[0];
    }

    private void Add(TemporaryRecordFile<GridSpan> run, int level)
    {
        cancellation.ThrowIfCancellationRequested();
        if (level == levels.Count)
        {
            levels.Add([]);
        }
        var pending = levels[level];
        pending.Add(run);
        if (pending.Count == FanIn)
        {
            var merged = Merge(pending);
            pending.Clear();
            Add(merged, level + 1);
        }
    }

    private TemporaryRecordFile<GridSpan> Merge(IReadOnlyList<TemporaryRecordFile<GridSpan>> inputs)
    {
        var merged = records.Write(Coalesce(Ordered(inputs)));
        foreach (var input in inputs)
        {
            records.Delete(input);
        }
        return merged;
    }

    public TemporaryRecordFile<GridSpan> MergeSorted(IEnumerable<IEnumerable<GridSpan>> inputs)
        => records.Write(Coalesce(Ordered(inputs)));

    private IEnumerable<GridSpan> Ordered(IReadOnlyList<TemporaryRecordFile<GridSpan>> inputs)
        => Ordered(inputs.Select(input => input.Read()));

    private IEnumerable<GridSpan> Ordered(IEnumerable<IEnumerable<GridSpan>> inputs)
    {
        var readers = new List<IEnumerator<GridSpan>>();
        var queue = new PriorityQueue<IEnumerator<GridSpan>, GridSpan>();
        try
        {
            foreach (var input in inputs)
            {
                var reader = input.GetEnumerator();
                readers.Add(reader);
                if (reader.MoveNext())
                {
                    queue.Enqueue(reader, reader.Current);
                }
            }
            while (queue.TryDequeue(out var reader, out var span))
            {
                yield return span;
                if (reader.MoveNext())
                {
                    queue.Enqueue(reader, reader.Current);
                }
            }
        }
        finally
        {
            foreach (var reader in readers)
            {
                reader.Dispose();
            }
        }
    }

    private IEnumerable<GridSpan> Coalesce(IEnumerable<GridSpan> sorted)
    {
        GridSpan? pending = null;
        var count = 0;
        foreach (var span in sorted)
        {
            if (count++ % 4096 == 0)
            {
                cancellation.ThrowIfCancellationRequested();
            }
            if (pending is { } previous)
            {
                if (previous.Joins(span))
                {
                    pending = previous.Union(span);
                    continue;
                }
                yield return previous;
            }
            pending = span;
        }
        if (pending is { } last)
        {
            yield return last;
        }
    }
}
