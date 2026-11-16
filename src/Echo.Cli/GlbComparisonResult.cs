namespace Echo.Cli;

internal sealed class GlbComparisonResult
{
    public required object Output
    {
        get; init;
    }
    public required object Reference
    {
        get; init;
    }
    public required string Comparison
    {
        get; init;
    }
}
