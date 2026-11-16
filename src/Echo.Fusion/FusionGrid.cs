namespace Echo.Fusion;

internal sealed class FusionGrid
{
    public double PixelFootprint
    {
        get; init;
    }
    public double RequestedVoxelSize
    {
        get; init;
    }
    public double VoxelSize
    {
        get; init;
    }
    public FusionCandidateGrid Keys { get; init; } = new([], 0, GridAxis.Z);
}
