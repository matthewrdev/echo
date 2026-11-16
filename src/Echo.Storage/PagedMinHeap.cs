namespace Echo.Storage;

// PriorityQueue owns a contiguous managed array and cannot accept external
// storage. This heap preserves its four-child ordering, including equal keys.
public sealed class PagedMinHeap<T>
{
    private readonly IList<HeapEntry<T>> entries;

    public PagedMinHeap(IList<HeapEntry<T>> entries)
    {
        this.entries = entries;
    }

    public int Count => entries.Count;

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
        entries[slot] = item;
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
        var slot = 0;
        while ((long)slot * 4 + 1 < entries.Count)
        {
            var child = slot * 4 + 1;
            var best = entries[child];
            var limit = Math.Min((long)child + 4, entries.Count);
            for (var candidate = child + 1; candidate < limit; candidate++)
            {
                var entry = entries[candidate];
                if (entry.Priority.CompareTo(best.Priority) < 0)
                {
                    child = candidate;
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
        return true;
    }
}
