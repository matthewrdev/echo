namespace Echo.Contracts;

public sealed class FusionResolutionReport
{
    public double PixelFootprint
    {
        get; set;
    }
    public double RequestedVoxelSize
    {
        get; set;
    }
    public long MemoryBudgetBytes
    {
        get; set;
    }
    public int MaximumGridSamples
    {
        get; set;
    }
    public int CandidateSamples
    {
        get; set;
    }
    public bool MemoryLimited
    {
        get; set;
    }
}
