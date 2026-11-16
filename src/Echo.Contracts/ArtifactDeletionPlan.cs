namespace Echo.Contracts;

public sealed class ArtifactDeletionPlan
{
    public string Token { get; set; } = "";
    public ArtifactDeletionKind Kind
    {
        get; set;
    }
    public long Bytes
    {
        get; set;
    }
    public List<string> Paths { get; set; } = [];
    public Dictionary<string, string> AffectedRuns { get; set; } = [];
    public List<string> RetainedRuns { get; set; } = [];
    public List<string> RemovedRuns { get; set; } = [];
    public DateTimeOffset? LogCutoff
    {
        get; set;
    }
}
