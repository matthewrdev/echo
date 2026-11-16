namespace Echo.Geometry;

public sealed class SurfaceTopology
{
    public IList<int> WeldedVertices { get; init; } = [];
    public IList<int> Neighbours { get; init; } = [];
}
