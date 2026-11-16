using Echo.Storage;

namespace Echo.Simplification;

// Keep slot order and reuse deleted slots in LIFO order, like HashSet<int>.
// Collapse candidate order must not depend on the chosen storage backend.
internal sealed class VertexFacesStore
{
    private readonly IList<AdjacencyHead> heads;
    private readonly IList<AdjacencySlot> slots;
    private readonly IDictionary<Edge, int> lookup;

    public VertexFacesStore(int count, WorkBuffers work)
    {
        heads = work.Filled(count, new AdjacencyHead(-1, -1, -1, 0), new AdjacencyHeadCodec());
        slots = work.List(new AdjacencySlotCodec());
        lookup = work.Map(new EdgeRecordCodec(), new IntRecordCodec());
    }

    public int Count(int vertex) => heads[vertex].Count;
    public bool Contains(int vertex, int face) => lookup.ContainsKey(new Edge(vertex, face));

    public void Add(int vertex, int face)
    {
        var key = new Edge(vertex, face);
        if (lookup.ContainsKey(key))
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
        lookup.Add(key, slot);
        heads[vertex] = head with
        {
            Count = head.Count + 1
        };
    }

    public bool Remove(int vertex, int face)
    {
        var key = new Edge(vertex, face);
        if (!lookup.TryGetValue(key, out var slot))
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
        lookup.Remove(key);
        return true;
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
