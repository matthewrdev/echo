using Echo.Geometry;

namespace Echo.Meshing.Repair;

internal sealed class SurfaceBoundaryLoop
{
    public List<int> Vertices { get; init; } = [];
    public V3 AdjacentNormal
    {
        get; init;
    }
}
