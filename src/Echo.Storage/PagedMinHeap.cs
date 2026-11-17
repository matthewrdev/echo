namespace Echo.Storage;

// PriorityQueue owns a contiguous managed array and cannot accept external
// storage. This heap preserves its four-child ordering, including equal keys.
public sealed class PagedMinHeap<T>
{
    private readonly IList<HeapEntry<T>> entries;
    private readonly IRecordList<HeapEntry<T>>? pagedEntries;
    private readonly HeapEntry<T>[] children = new HeapEntry<T>[4];

    public PagedMinHeap(IList<HeapEntry<T>> entries)
    {
        this.entries = entries;
        pagedEntries = entries as IRecordList<HeapEntry<T>>;
    }

    public int Count => entries.Count;

    public int Retain(Predicate<T> keep, CancellationToken cancellation, Action<long, long>? progress = null)
    {
        var originalCount = entries.Count;
        var parentCount = originalCount < 2 ? 0 : (originalCount - 2) / 4 + 1;
        var total = (long)originalCount + parentCount;
        var retained = 0;
        progress?.Invoke(0, total);
        for (var index = 0; index < originalCount; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var item = entries[index];
            if (keep(item.Value))
            {
                entries[retained++] = item;
            }
            progress?.Invoke(index + 1, total);
        }
        while (entries.Count > retained)
        {
            cancellation.ThrowIfCancellationRequested();
            entries.RemoveAt(entries.Count - 1);
        }
        // Removed entries also remove parent work. Count these as skipped so
        // the bounded scan/rebuild has a stable denominator throughout.
        var remainingParents = retained < 2 ? 0 : (retained - 2) / 4 + 1;
        var completed = total - remainingParents;
        for (var parent = remainingParents - 1; parent >= 0; parent--)
        {
            cancellation.ThrowIfCancellationRequested();
            MoveDown(parent, entries[parent]);
            progress?.Invoke(++completed, total);
        }
        progress?.Invoke(total, total);
        return originalCount - retained;
    }

    public void Enqueue(T value, double priority)
    {
        var item = new HeapEntry<T>(value, priority);
        var slot = entries.Count;
        entries.Add(item);
        while (slot > 0)
        {
            var parent = (slot - 1) / 4;
            var previous = entries[parent];
            if (priority.CompareTo(previous.Priority) >= 0)
            {
                break;
            }
            entries[slot] = previous;
            slot = parent;
        }
        if (slot != entries.Count - 1)
        {
            entries[slot] = item;
        }
    }

    public bool TryDequeue(out T value, out double priority)
    {
        if (entries.Count == 0)
        {
            value = default!;
            priority = default;
            return false;
        }
        var first = entries[0];
        value = first.Value;
        priority = first.Priority;
        var replacement = entries[^1];
        entries.RemoveAt(entries.Count - 1);
        if (entries.Count == 0)
        {
            return true;
        }
        MoveDown(0, replacement);
        return true;
    }

    private void MoveDown(int slot, HeapEntry<T> replacement)
    {
        while ((long)slot * 4 + 1 < entries.Count)
        {
            var first = slot * 4 + 1;
            var count = (int)Math.Min(4L, entries.Count - (long)first);
            if (pagedEntries != null)
            {
                // Siblings are contiguous. Decode all four under one cache
                // access instead of acquiring the shared lock per candidate.
                pagedEntries.ReadRange(first, children.AsSpan(0, count));
            }
            else
            {
                for (var index = 0; index < count; index++)
                {
                    children[index] = entries[first + index];
                }
            }
            var child = first;
            var best = children[0];
            for (var candidate = 1; candidate < count; candidate++)
            {
                var entry = children[candidate];
                if (entry.Priority.CompareTo(best.Priority) < 0)
                {
                    child = first + candidate;
                    best = entry;
                }
            }
            if (replacement.Priority.CompareTo(best.Priority) <= 0)
            {
                break;
            }
            entries[slot] = best;
            slot = child;
        }
        entries[slot] = replacement;
    }
}
