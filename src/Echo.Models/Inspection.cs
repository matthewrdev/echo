namespace Echo.Models;

public sealed class Inspection
{
    public int SchemaVersion
    {
        get; set;
    }
    public string ProjectRoot { get; set; } = "";
    public ProjectData Project { get; set; } = new();
    public RunData? Run
    {
        get; set;
    }
    public ReconstructionScene? Reconstruction
    {
        get; set;
    }
    public Surface? Surface
    {
        get; set;
    }
    public DenseCloudReport? Cloud
    {
        get; set;
    }
    public List<string> Warnings { get; set; } = [];
}
