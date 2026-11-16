namespace Echo.Pipeline;

internal sealed class CliffDepthReport
{
    public required string ImageId
    {
        get; init;
    }
    public required int SearchedSamples
    {
        get; init;
    }
    public required int RecoveredSamples
    {
        get; init;
    }
    public required int SourceImageWidth
    {
        get; init;
    }
    public required int Neighbours
    {
        get; init;
    }
    public required int PreservedSamples
    {
        get; init;
    }
    public required string Method
    {
        get; init;
    }
}
