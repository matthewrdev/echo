namespace Echo.Contracts;

public sealed class ArtifactDeletionPlan
{
    public string Token { get; set; } = "";
    public bool PurgeAll
    {
        get; set;
    }
    public long Bytes
    {
        get; set;
    }
    public List<string> Paths { get; set; } = [];
    public Dictionary<string, string> AffectedRuns { get; set; } = [];
}
