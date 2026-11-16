using Echo.Contracts;

namespace Echo.Pipeline;

internal sealed class FusionReport
{
    public required double VoxelSize
    {
        get; init;
    }
    public required int InputSamples
    {
        get; init;
    }
    public required int ConsistentSamples
    {
        get; init;
    }
    public required int Voxels
    {
        get; init;
    }
    public required FusionResolutionReport? Resolution
    {
        get; init;
    }
    public required FusionIslandReport IslandTrimming
    {
        get; init;
    }
    public required string Method
    {
        get; init;
    }
}
