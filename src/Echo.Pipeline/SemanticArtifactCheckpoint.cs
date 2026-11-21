using Echo.Models;

namespace Echo.Pipeline;

public sealed class SemanticArtifactCheckpoint
{
    public required string Unit
    {
        get; init;
    }
    public required SemanticMap Map
    {
        get; init;
    }
    public bool Reused
    {
        get; init;
    }
}
