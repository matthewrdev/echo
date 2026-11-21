using System.Collections;

namespace Echo.Simplification;

internal struct VertexFaceEnumerator : IEnumerator<int>
{
    private readonly IList<AdjacencySlot> slots;
    private int next;

    public VertexFaceEnumerator(IList<AdjacencySlot> slots, int first)
    {
        this.slots = slots;
        next = first;
        Current = default;
    }

    public int Current
    {
        get; private set;
    }
    object IEnumerator.Current => Current;

    public bool MoveNext()
    {
        while (next >= 0)
        {
            var item = slots[next];
            next = item.Next;
            if (item.Face >= 0)
            {
                Current = item.Face;
                return true;
            }
        }

        return false;
    }

    public void Reset() => throw new NotSupportedException();
    public void Dispose()
    {
    }
}
