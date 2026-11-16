using Echo.Geometry;

namespace Echo.Simplification;

internal sealed class Vertex
{
    public int Id
    {
        get; init;
    }
    public V3 Position
    {
        get; set;
    }
    public double[] Quadric { get; } = new double[16];
    public int PlaneCount
    {
        get; set;
    }
    public ICollection<int> Faces { get; init; } = new HashSet<int>();
    public int Revision
    {
        get; set;
    }
    public bool Removed
    {
        get; set;
    }
}
