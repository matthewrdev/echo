using Echo.Geometry;

namespace Echo.Simplification;

internal sealed class Vertex
{
    public V3 Position
    {
        get; set;
    }
    public double[] Quadric { get; } = new double[16];
    public HashSet<int> Faces { get; } = [];
    public int Revision
    {
        get; set;
    }
    public bool Removed
    {
        get; set;
    }
}
