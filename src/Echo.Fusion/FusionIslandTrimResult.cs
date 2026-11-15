using Echo.Contracts;

namespace Echo.Fusion;

public sealed class FusionIslandTrimResult
{
    public FusedVolume Volume { get; init; } = new();
    public int? InputRegions { get; init; } = null;
    public int RemovedRegions { get; init; } = 0;
    public int RemovedSamples { get; init; } = 0;
}
