namespace Echo.Contracts;

public sealed class DenseCloudOverview
{
    public int InputSamples
    {
        get; set;
    }
    public int RetainedSamples
    {
        get; set;
    }
    public List<string> ImageIds { get; set; } = [];

    public DenseCloudReport ToReport() => new()
    {
        InputSamples = InputSamples,
        RetainedSamples = RetainedSamples,
        ImageIds = ImageIds
    };
}
