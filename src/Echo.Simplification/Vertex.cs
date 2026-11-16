using Echo.Geometry;

namespace Echo.Simplification;

internal struct Vertex
{
    private Quadric quadric;

    public int Id
    {
        get; init;
    }
    public V3 Position
    {
        get; set;
    }
    public double this[int coefficient]
    {
        readonly get => quadric[coefficient];
        set => quadric[coefficient] = value;
    }
    public int PlaneCount
    {
        get; set;
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
