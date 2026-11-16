namespace Echo.Pipeline;

internal sealed class SemanticImageInputs
{
    public required string Id
    {
        get; init;
    }
    public required string Hash
    {
        get; init;
    }
    public required string SourceHash
    {
        get; init;
    }
    public required string Fingerprint
    {
        get; init;
    }
}
