namespace Echo.Pipeline;

internal sealed class CliffDepthInputs
{
    public required string[] OriginalUnits
    {
        get; init;
    }
    public required string ImageId
    {
        get; init;
    }
    public required string Alignment
    {
        get; init;
    }
    public required string Semantics
    {
        get; init;
    }
    public required int DepthNeighbours
    {
        get; init;
    }
    public required bool DepthSuppressSky
    {
        get; init;
    }
}
