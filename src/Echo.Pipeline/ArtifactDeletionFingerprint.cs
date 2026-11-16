namespace Echo.Pipeline;

internal sealed class ArtifactDeletionFingerprint
{
    public required string Token
    {
        get; init;
    }
    public required string? Artifact
    {
        get; init;
    }
    public required List<string> Paths
    {
        get; init;
    }
    public required Dictionary<string, string> AffectedRuns
    {
        get; init;
    }
}
