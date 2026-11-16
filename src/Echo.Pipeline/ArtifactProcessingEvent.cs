using Echo.Contracts;

namespace Echo.Pipeline;

internal sealed class ArtifactProcessingEvent
{
    public required string Stage
    {
        get; init;
    }
    public required string Artifact
    {
        get; init;
    }
    public required ArtifactProcessingReference Processing
    {
        get; init;
    }
}
