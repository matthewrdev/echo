namespace Echo.Models;

public sealed class WorkspaceRunSnapshot
{
    public string Id { get; init; } = "";
    public string Status { get; init; } = "";
    public string Target { get; init; } = "";
    public ProjectData? Snapshot
    {
        get; init;
    }
    public string SnapshotToken { get; init; } = "";
    public Dictionary<string, string> Artifacts { get; init; } = [];
    public string? Error
    {
        get; init;
    }
    public RunProgress? Progress
    {
        get; init;
    }
    public int StorageVersion
    {
        get; init;
    }
}
