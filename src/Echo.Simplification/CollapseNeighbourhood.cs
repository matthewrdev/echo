namespace Echo.Simplification;

internal sealed class CollapseNeighbourhood
{
    public int Vertex
    {
        get; set;
    }
    public HashSet<int> Neighbours { get; } = [];
}
