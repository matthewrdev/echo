using Echo.Contracts;

namespace Echo.Pipeline;

internal sealed class FusionInputs
{
    public required string CloudPath
    {
        get; init;
    }
    public required List<string> DepthUnits
    {
        get; init;
    }
    public required string Alignment
    {
        get; init;
    }
    public required int MaximumGridSamples
    {
        get; init;
    }
    public required int FusionVersion
    {
        get; init;
    }
    public required ComputeBackend ComputeBackend
    {
        get; init;
    }
    public required string ComputeImplementation
    {
        get; init;
    }
    public required double CloudDepthTolerance
    {
        get; init;
    }
    public required double FusionDetailMultiplier
    {
        get; init;
    }
    public required double FusionVoxelSize
    {
        get; init;
    }
    public required bool FusionAllowCoarserResolution
    {
        get; init;
    }
    public required IslandTrimMode FusionIslandMode
    {
        get; init;
    }
    public required double FusionIslandMinimumSizePercent
    {
        get; init;
    }
}
