namespace Echo.Pipeline;

internal sealed class FusionChunkInputs
{
    public required object Inputs
    {
        get; init;
    }
    public required long First
    {
        get; init;
    }
    public required int Count
    {
        get; init;
    }
    public required int FormatVersion
    {
        get; init;
    }
}
