namespace Echo.Geometry;

public sealed class SurfaceTopology
{
    public int[] WeldedVertices { get; init; } = [];
    public int[] Neighbours { get; init; } = [];
}
