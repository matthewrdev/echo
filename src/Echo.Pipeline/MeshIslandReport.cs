using Echo.Contracts;

namespace Echo.Pipeline;

internal sealed class MeshIslandReport
{
    public required IslandTrimMode Mode
    {
        get; init;
    }
    public required double MinimumAreaPercent
    {
        get; init;
    }
    public required int? InputComponents
    {
        get; init;
    }
    public required int RemovedComponents
    {
        get; init;
    }
    public required int RemovedFaces
    {
        get; init;
    }
    public required double RemovedArea
    {
        get; init;
    }
}
