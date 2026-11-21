using Echo.Models;

namespace Echo.Meshing.Repair;

public sealed class SurfaceIslandTrimResult
{
    public Surface Surface { get; init; } = new();
    public int? InputComponents
    {
        get; init;
    }
    public int RemovedComponents
    {
        get; init;
    }
    public int RemovedFaces
    {
        get; init;
    }
    public double RemovedArea
    {
        get; init;
    }
}
