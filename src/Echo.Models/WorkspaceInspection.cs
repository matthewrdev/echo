namespace Echo.Models;

public sealed class WorkspaceInspection
{
    public int SchemaVersion
    {
        get; init;
    }
    public string ProjectRoot { get; init; } = "";
    public ProjectData? Project
    {
        get; init;
    }
    public WorkspaceRunSnapshot? Run
    {
        get; init;
    }
    public ReconstructionScene? Reconstruction
    {
        get; init;
    }
    public Surface? Surface
    {
        get; init;
    }
    public DenseCloudReport? Cloud
    {
        get; init;
    }
    public List<string> Warnings { get; init; } = [];
}
