namespace Echo.Fusion;

internal sealed class FusionGridBatch
{
    private readonly HashSet<GridSpan> ranges;
    private readonly GridSpan[] spans;
    private readonly FusionGridRuns runs;
    private readonly GridAxis axis;
    private GridSpan? pending;

    public FusionGridBatch(int capacity, FusionGridRuns runs, GridAxis axis)
    {
        ranges = new HashSet<GridSpan>(capacity);
        spans = new GridSpan[capacity];
        this.runs = runs;
        this.axis = axis;
    }

    public void Add(Grid world)
    {
        var cell = FusionGridAxes.ToStorage(world, axis);
        if (pending is { } previous)
        {
            var sameColumn = cell.X == previous.X && cell.Y == previous.Y;
            var adjacent = cell.Z >= (long)previous.FirstZ - 1 && cell.Z <= (long)previous.LastZ + 1;
            if (sameColumn && adjacent)
            {
                pending = new GridSpan(cell.X, cell.Y, Math.Min(previous.FirstZ, cell.Z), Math.Max(previous.LastZ, cell.Z));
                return;
            }
        }
        CommitPending();
        pending = new GridSpan(cell.X, cell.Y, cell.Z, cell.Z);
    }

    public void Flush()
    {
        CommitPending();
        FlushRanges();
    }

    private void CommitPending()
    {
        if (pending is { } span)
        {
            ranges.Add(span);
            pending = null;
            if (ranges.Count == spans.Length)
            {
                FlushRanges();
            }
        }
    }

    private void FlushRanges()
    {
        var count = ranges.Count;
        if (count == 0)
        {
            return;
        }
        ranges.CopyTo(spans);
        Array.Sort(spans, 0, count);
        runs.Add(spans.Take(count));
        ranges.Clear();
    }
}
