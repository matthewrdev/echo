namespace Echo.Contracts;

public sealed class FusedVolume
{
    public double VoxelSize
    {
        get; set;
    }
    public int InputSamples
    {
        get; set;
    }
    public int ConsistentSamples
    {
        get; set;
    }
    public List<VolumeSample> Samples { get; set; } = [];
    public FusionResolutionReport? Resolution
    {
        get; set;
    }
}
