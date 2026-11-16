namespace Echo.Contracts;

public sealed class ReconstructionOverview
{
    public string Frame { get; set; } = "";
    public GeoPosition? Origin
    {
        get; set;
    }
    public List<CameraPose> Cameras { get; set; } = [];
    public List<string> Unplaced { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public BundleReport? Bundle
    {
        get; set;
    }

    public ReconstructionScene ToScene() => new()
    {
        Frame = Frame,
        Origin = Origin,
        Cameras = Cameras,
        Unplaced = Unplaced,
        Warnings = Warnings,
        Bundle = Bundle
    };
}
