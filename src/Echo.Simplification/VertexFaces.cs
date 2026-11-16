using System.Collections;

namespace Echo.Simplification;

internal sealed class VertexFaces : ICollection<int>
{
    private readonly VertexFacesStore store;
    private readonly int vertex;

    public VertexFaces(VertexFacesStore store, int vertex)
    {
        this.store = store;
        this.vertex = vertex;
    }

    public int Count => store.Count(vertex);
    public bool IsReadOnly => false;
    public void Add(int item) => store.Add(vertex, item);
    public bool Remove(int item) => store.Remove(vertex, item);
    public bool Contains(int item) => store.Contains(vertex, item);
    public IEnumerator<int> GetEnumerator() => store.Faces(vertex).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public void Clear()
    {
        foreach (var item in this.ToArray())
        {
            Remove(item);
        }
    }
    public void CopyTo(int[] array, int arrayIndex)
    {
        foreach (var item in this)
        {
            array[arrayIndex++] = item;
        }
    }
}
