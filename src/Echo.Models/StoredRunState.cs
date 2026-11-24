namespace Echo.Models;

public sealed class StoredRunState
{
    public int StorageVersion
    {
        get; set;
    }
    public string Id { get; set; } = "";
    public string Status { get; set; } = "running";
    public string Target { get; set; } = "alignment";
    public DateTimeOffset Created
    {
        get; set;
    }
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

    public StoredRunState()
    {
    }

    public StoredRunState(RunData run)
    {
        StorageVersion = run.StorageVersion;
        Id = run.Id;
        Status = run.Status;
        Target = run.Target;
        Created = run.Created;
        Artifacts = run.Artifacts;
        ProcessingMetadataFile = run.ProcessingMetadataFile;
        Error = run.Error;
        Progress = run.Progress;
    }

    public RunData WithSnapshot(ProjectData snapshot) => new()
    {
        StorageVersion = StorageVersion,
        Id = Id,
        Status = Status,
        Target = Target,
        Snapshot = snapshot,
        Created = Created,
        Artifacts = Artifacts,
        ProcessingMetadataFile = ProcessingMetadataFile,
        Error = Error,
        Progress = Progress
    };
}
