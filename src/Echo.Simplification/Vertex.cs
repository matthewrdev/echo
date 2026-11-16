using Echo.Geometry;

namespace Echo.Simplification;

internal sealed class Vertex
{
    private Quadric quadric;

    public Vertex(ICollection<int>? faces = null)
    {
        Faces = faces ?? new HashSet<int>();
    }

    public int Id
    {
        get; init;
    }
    public V3 Position
    {
        get; set;
    }
    public Span<double> Quadric => quadric;
    public int PlaneCount
    {
        get; set;
    }
    public ICollection<int> Faces
    {
        get;
    }
    public int Revision
    {
        get; set;
    }
    public bool Removed
    {
        get; set;
    }
}
