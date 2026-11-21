using Echo.Models;

namespace Echo.Storage.Artifacts;

public sealed class BulkArtifactHeader
{
    public string Storage { get; set; } = "";
    public string Kind { get; set; } = "";
    public string ImageId { get; set; } = "";
    public int Width
    {
        get; set;
    }
    public int Height
    {
        get; set;
    }
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
    public FusionResolutionReport? Resolution
    {
        get; set;
    }
    public List<string> Images { get; set; } = [];
    public ReconstructionOverview? Scene
    {
        get; set;
    }
    public Dictionary<string, string> Buffers { get; set; } = [];
}
