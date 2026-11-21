namespace Echo.Models;

public sealed class ReconstructionScene
{
    public string Frame { get; set; } = "local: x east, y up, z south; metres when georeferenced";
    public GeoPosition? Origin
    {
        get; set;
    }
    public List<CameraPose> Cameras { get; set; } = [];
    public IList<CloudPoint> Points { get; set; } = new List<CloudPoint>();
    public List<string> Unplaced { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public BundleReport? Bundle
    {
        get; set;
    }
}
