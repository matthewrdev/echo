using Echo.Storage;

namespace Echo.Simplification;

// Keep slot order and reuse deleted slots in LIFO order, like HashSet<int>.
// Collapse candidate order must not depend on the chosen storage backend.
internal sealed class VertexFacesStore
{
    private readonly IList<AdjacencyHead> heads;
    private readonly IList<AdjacencySlot> slots;

    public VertexFacesStore(int count, WorkBuffers work, CancellationToken cancellation = default, Action<int>? progress = null)
    {
        heads = work.List(new AdjacencyHeadCodec());
        for (var index = 0; index < count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            heads.Add(new AdjacencyHead(-1, -1, -1, 0));
            progress?.Invoke(index + 1);
        }
        slots = work.List(new AdjacencySlotCodec());
    }

    public int Count(int vertex) => heads[vertex].Count;
    public bool Contains(int vertex, int face) => Find(vertex, face) >= 0;

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
            slots[slot] = old with
            {
                Face = face,
                FreeNext = -1
            };
            head = head with
            {
                Free = old.FreeNext
            };
        }
        else
        {
            slot = slots.Count;
            slots.Add(new AdjacencySlot(face, -1, -1));
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
        slots[slot] = slots[slot] with
        {
            Face = -1,
            FreeNext = head.Free
        };
        heads[vertex] = head with
        {
            Free = slot,
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

    public IEnumerable<int> Faces(int vertex)
    {
        var slot = heads[vertex].First;
        while (slot >= 0)
        {
            var item = slots[slot];
            if (item.Face >= 0)
            {
                yield return item.Face;
            }
            slot = item.Next;
        }
    }
}
