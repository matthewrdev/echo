using Echo.Storage.Records;
using Echo.Storage.Resources;
using Echo.Storage;

namespace Echo.Simplification;

// One live candidate per undirected edge. Stable handles make heap movements
// update direct slots rather than hashing endpoints at every level. Removed
// links are recycled, so storage follows the peak live edge count.
internal sealed class CollapseCandidateQueue
{
    private readonly IList<int> buckets;
    private readonly IList<CandidateQueueLink> links;
    private readonly IList<IndexedCandidate> entries;
    private readonly IRecordList<IndexedCandidate>? pagedEntries;
    private int free = -1;

    public CollapseCandidateQueue(int expectedEdges, WorkBuffers? work, CancellationToken cancellation)
    {
        var capacity = 16;
        while (capacity < expectedEdges && capacity < 1 << 26)
        {
            capacity *= 2;
        }

        buckets = work?.Filled(capacity, -1, new IntRecordCodec(), cancellation) ?? Enumerable.Repeat(-1, capacity).ToArray();
        links = work?.List(new CandidateQueueLinkCodec()) ?? new List<CandidateQueueLink>();
        entries = work?.List(new IndexedCandidateCodec()) ?? new List<IndexedCandidate>();
        pagedEntries = entries as IRecordList<IndexedCandidate>;
    }

    public int Count => entries.Count;
    internal int AllocatedHandles => links.Count;

    public bool Refresh(int a, int b, int revisionA, int revisionB)
    {
        if (a > b)
        {
            return Refresh(b, a, revisionB, revisionA);
        }

        for (var handle = buckets[Bucket(a, b)]; handle >= 0;)
        {
            var link = links[handle];
            if (link.A == a && link.B == b)
            {
                var item = entries[link.Slot];
                entries[link.Slot] = item with
                {
                    Value = item.Value with
                    {
                        RevisionA = revisionA,
                        RevisionB = revisionB
                    }
                };
                return true;
            }

            handle = link.Next;
        }

        return false;
    }

    public void Retain(Func<Candidate, bool> predicate, CancellationToken cancellation)
    {
        var retained = 0;
        for (var index = 0; index < entries.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var item = entries[index];
            if (predicate(item.Value))
            {
                Write(retained++, item);
                continue;
            }

            var bucket = Bucket(item.Value.A, item.Value.B);
            var previous = -1;
            for (var handle = buckets[bucket]; handle >= 0;)
            {
                var link = links[handle];
                if (handle == item.Handle)
                {
                    if (previous < 0)
                    {
                        buckets[bucket] = link.Next;
                    }
                    else
                    {
                        links[previous] = links[previous] with
                        {
                            Next = link.Next
                        };
                    }

                    links[handle] = new CandidateQueueLink(-1, -1, free, -1);
                    free = handle;
                    break;
                }

                previous = handle;
                handle = link.Next;
            }
        }

        while (entries.Count > retained)
        {
            entries.RemoveAt(entries.Count - 1);
        }

        for (var index = (entries.Count - 2) / 4; index >= 0 && entries.Count > 1; index--)
        {
            cancellation.ThrowIfCancellationRequested();
            MoveDown(index, entries[index]);
        }
    }

    public void Offer(Candidate candidate, double priority)
    {
        var bucket = Bucket(candidate.A, candidate.B);
        for (var handle = buckets[bucket]; handle >= 0;)
        {
            var link = links[handle];
            if (link.A == candidate.A && link.B == candidate.B)
            {
                var updated = new IndexedCandidate(candidate, priority, handle);
                if (entries[link.Slot].Priority.CompareTo(priority) == 0)
                {
                    // Neighbour topology can change the revision stamps while
                    // leaving this edge's cost untouched. Its heap slot and
                    // handle remain valid, including equal-priority ordering.
                    entries[link.Slot] = updated;
                }
                else
                {
                    Replace(link.Slot, updated);
                }

                return;
            }

            handle = link.Next;
        }

        var next = free;
        var added = new CandidateQueueLink(candidate.A, candidate.B, buckets[bucket], entries.Count);
        if (next < 0)
        {
            next = links.Count;
            links.Add(added);
        }
        else
        {
            free = links[next].Next;
            links[next] = added;
        }

        buckets[bucket] = next;
        var item = new IndexedCandidate(candidate, priority, next);
        entries.Add(item);
        MoveUp(entries.Count - 1, item);
    }

    public bool Remove(int a, int b)
    {
        var first = Math.Min(a, b);
        var second = Math.Max(a, b);
        var bucket = Bucket(first, second);
        var previous = -1;
        for (var handle = buckets[bucket]; handle >= 0;)
        {
            var link = links[handle];
            if (link.A == first && link.B == second)
            {
                if (previous < 0)
                {
                    buckets[bucket] = link.Next;
                }
                else
                {
                    links[previous] = links[previous] with
                    {
                        Next = link.Next
                    };
                }

                links[handle] = new CandidateQueueLink(-1, -1, free, -1);
                free = handle;
                var last = entries[^1];
                entries.RemoveAt(entries.Count - 1);
                if (link.Slot < entries.Count)
                {
                    Replace(link.Slot, last);
                }

                return true;
            }

            previous = handle;
            handle = link.Next;
        }

        return false;
    }

    public bool TryDequeue(out Candidate candidate)
    {
        if (entries.Count == 0)
        {
            candidate = default;
            return false;
        }

        candidate = entries[0].Value;
        Remove(candidate.A, candidate.B);
        return true;
    }

    private void Replace(int slot, IndexedCandidate item)
    {
        if (slot > 0 && item.Priority.CompareTo(entries[(slot - 1) / 4].Priority) < 0)
        {
            MoveUp(slot, item);
        }
        else
        {
            MoveDown(slot, item);
        }
    }

    private void MoveUp(int slot, IndexedCandidate item)
    {
        while (slot > 0)
        {
            var parent = (slot - 1) / 4;
            var previous = entries[parent];
            if (item.Priority.CompareTo(previous.Priority) >= 0)
            {
                break;
            }

            Write(slot, previous);
            slot = parent;
        }

        Write(slot, item);
    }

    private void MoveDown(int slot, IndexedCandidate item)
    {
        Span<IndexedCandidate> children = stackalloc IndexedCandidate[4];
        while ((long)slot * 4 + 1 < entries.Count)
        {
            var first = slot * 4 + 1;
            var count = (int)Math.Min(4L, entries.Count - (long)first);
            if (pagedEntries != null)
            {
                pagedEntries.ReadRange(first, children[..count]);
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
            for (var index = 1; index < count; index++)
            {
                if (children[index].Priority.CompareTo(best.Priority) < 0)
                {
                    child = first + index;
                    best = children[index];
                }
            }

            if (item.Priority.CompareTo(best.Priority) <= 0)
            {
                break;
            }

            Write(slot, best);
            slot = child;
        }

        Write(slot, item);
    }

    private void Write(int slot, IndexedCandidate item)
    {
        entries[slot] = item;
        links[item.Handle] = links[item.Handle] with
        {
            Slot = slot
        };
    }

    private int Bucket(int a, int b)
    {
        var hash = unchecked((uint)a * 0x9e3779b9u + (uint)b);
        hash ^= hash >> 16;
        hash *= 0x85ebca6bu;
        hash ^= hash >> 13;
        return (int)(hash & (uint)(buckets.Count - 1));
    }
}
