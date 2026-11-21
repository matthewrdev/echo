namespace Echo.Models;

public sealed class RunData
{
    public int StorageVersion
    {
        get; set;
    }
    public string Id { get; set; } = "";
    public string Status { get; set; } = "running";
    public string Target { get; set; } = "alignment";
    public ProjectData Snapshot { get; set; } = new();
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    public Dictionary<string, string> Artifacts { get; set; } = [];
    public string? ProcessingMetadataFile
    {
        get; set;
    }
    public string? Error
    {
        get; set;
    }
    public RunProgress? Progress
    {
        get; set;
    }
}
