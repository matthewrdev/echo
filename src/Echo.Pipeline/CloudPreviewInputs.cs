namespace Echo.Pipeline;

internal sealed class CloudPreviewInputs
{
    public required int PreviewVersion
    {
        get; init;
    }
    public required object Inputs
    {
        get; init;
    }
    public required int InputSamples
    {
        get; init;
    }
    public required int RetainedSamples
    {
        get; init;
    }
}
