using System.Collections;

namespace Echo.Simplification;

internal readonly struct VertexFaceEnumerable : IEnumerable<int>
{
    private readonly IList<AdjacencySlot> slots;
    private readonly int first;

    public VertexFaceEnumerable(IList<AdjacencySlot> slots, int first)
    {
        this.slots = slots;
        this.first = first;
    }

    public VertexFaceEnumerator GetEnumerator() => new(slots, first);
    IEnumerator<int> IEnumerable<int>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
