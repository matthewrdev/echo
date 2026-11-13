namespace Echo.Contracts;

public sealed class DenseCloudReport
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
    public List<CloudPoint> Preview { get; set; } = [];
}
