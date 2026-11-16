namespace Echo.Pipeline;

internal sealed class ArtifactEvent
{
    public required string Stage
    {
        get; init;
    }
    public required string Artifact
    {
        get; init;
    }
}
