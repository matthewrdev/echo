using Echo.Storage.Resources;
using Echo.Storage;

namespace Echo.Simplification;

// Keep slot order and reuse deleted slots in LIFO order, like HashSet<int>.
// Collapse candidate order must not depend on the chosen storage backend.
internal sealed class VertexFacesStore
{
    private readonly IList<AdjacencyHead> heads;
    private readonly IList<AdjacencySlot> slots;

    public VertexFacesStore(int count, WorkBuffers? work, CancellationToken cancellation = default, Action<int>? progress = null)
    {
        heads = work?.List(new AdjacencyHeadCodec()) ?? new List<AdjacencyHead>(count);
        for (var index = 0; index < count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            heads.Add(new AdjacencyHead(-1, -1, -1, 0));
            progress?.Invoke(index + 1);
        }
        slots = work?.List(new AdjacencySlotCodec()) ?? new List<AdjacencySlot>();
    }

    public int Count(int vertex) => heads[vertex].Count;
    public bool Contains(int vertex, int face) => Find(vertex, face) >= 0;

    public void Initialize(IList<FaceVertices> faces, CancellationToken cancellation)
    {
        if (slots.Count != 0)
        {
            throw new InvalidOperationException("Adjacency is already initialized.");
        }
        for (var index = 0; index < faces.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var face = faces[index];
            for (var corner = 0; corner < 3; corner++)
            {
                var vertex = face[corner];
                var repeatsFirst = corner > 0 && face[0] == vertex;
                var repeatsSecond = corner > 1 && face[1] == vertex;
                var duplicate = repeatsFirst || repeatsSecond;
                if (!duplicate)
                {
                    heads[vertex] = heads[vertex] with
                    {
                        Count = checked(heads[vertex].Count + 1)
                    };
                }
            }
        }
        for (var vertex = 0; vertex < heads.Count; vertex++)
        {
            cancellation.ThrowIfCancellationRequested();
            var count = heads[vertex].Count;
            if (count == 0)
            {
                continue;
            }
            var start = slots.Count;
            for (var slot = 0; slot < count; slot++)
            {
                slots.Add(default);
            }
            heads[vertex] = new AdjacencyHead(start, slots.Count - 1, start, count);
        }
        // Each vertex's initial chain occupies a contiguous range, retaining
        // face encounter order without searches or scattered tail rewrites.
        for (var index = 0; index < faces.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var face = faces[index];
            for (var corner = 0; corner < 3; corner++)
            {
                var vertex = face[corner];
                var repeatsFirst = corner > 0 && face[0] == vertex;
                var repeatsSecond = corner > 1 && face[1] == vertex;
                var duplicate = repeatsFirst || repeatsSecond;
                if (duplicate)
                {
                    continue;
                }
                var head = heads[vertex];
                var slot = head.Free;
                slots[slot] = new AdjacencySlot(index, slot == head.Last ? -1 : slot + 1, -1, slot == head.First ? -1 : slot - 1);
                heads[vertex] = head with
                {
                    Free = slot == head.Last ? -1 : slot + 1
                };
            }
        }
    }

    public void Add(int vertex, int face)
    {
        if (Find(vertex, face) >= 0)
        {
            return;
        }
        var head = heads[vertex];
        var slot = head.Free;
        if (slot >= 0)
        {
            var old = slots[slot];
            // Slot IDs retain encounter order. Reinsert a recycled slot among
            // live slots so enumeration never walks deleted records.
            var previous = -1;
            var next = head.First;
            while (next >= 0 && next < slot)
            {
                previous = next;
                next = slots[next].Next;
            }
            slots[slot] = new AdjacencySlot(face, next, -1, previous);
            if (previous >= 0)
            {
                slots[previous] = slots[previous] with
                {
                    Next = slot
                };
            }
            if (next >= 0)
            {
                slots[next] = slots[next] with
                {
                    Previous = slot
                };
            }
            head = head with
            {
                Free = old.FreeNext,
                First = previous < 0 ? slot : head.First,
                Last = next < 0 ? slot : head.Last
            };
        }
        else
        {
            slot = slots.Count;
            slots.Add(new AdjacencySlot(face, -1, -1, head.Last));
            if (head.Last >= 0)
            {
                slots[head.Last] = slots[head.Last] with
                {
                    Next = slot
                };
            }
            head = head with
            {
                First = head.First < 0 ? slot : head.First,
                Last = slot
            };
        }
        heads[vertex] = head with
        {
            Count = head.Count + 1
        };
    }

    public bool Remove(int vertex, int face)
    {
        var slot = Find(vertex, face);
        if (slot < 0)
        {
            return false;
        }
        var head = heads[vertex];
        var removed = slots[slot];
        if (removed.Previous >= 0)
        {
            slots[removed.Previous] = slots[removed.Previous] with
            {
                Next = removed.Next
            };
        }
        if (removed.Next >= 0)
        {
            slots[removed.Next] = slots[removed.Next] with
            {
                Previous = removed.Previous
            };
        }
        slots[slot] = removed with
        {
            Face = -1,
            FreeNext = head.Free
        };
        heads[vertex] = head with
        {
            Free = slot,
            First = removed.Previous < 0 ? removed.Next : head.First,
            Last = removed.Next < 0 ? removed.Previous : head.Last,
            Count = head.Count - 1
        };
        return true;
    }

    // A vertex normally touches only a handful of faces. Search its compact
    // paged slot chain instead of maintaining a second, global SQL index for
    // every adjacency insertion, removal and membership test.
    private int Find(int vertex, int face)
    {
        var slot = heads[vertex].First;
        while (slot >= 0)
        {
            var item = slots[slot];
            if (item.Face == face)
            {
                return slot;
            }
            slot = item.Next;
        }
        return -1;
    }

    public VertexFaceEnumerable Faces(int vertex) => new(slots, heads[vertex].First);
}
