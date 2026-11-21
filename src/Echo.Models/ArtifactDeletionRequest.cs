namespace Echo.Models;

public sealed class ArtifactDeletionRequest
{
    public ArtifactDeletionKind Kind
    {
        get; init;
    }
    public string? Artifact
    {
        get; init;
    }
    public IReadOnlyList<string>? RetainedRuns
    {
        get; init;
    }
}
