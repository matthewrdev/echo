using Echo.Contracts;

namespace Echo.Pipeline;

internal sealed class FusionIslandReport
{
    public required IslandTrimMode Mode
    {
        get; init;
    }
    public required double MinimumSizePercent
    {
        get; init;
    }
    public required int? InputRegions
    {
        get; init;
    }
    public required int RemovedRegions
    {
        get; init;
    }
    public required int RemovedSamples
    {
        get; init;
    }
}
