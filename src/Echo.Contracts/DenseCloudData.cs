namespace Echo.Contracts;

public sealed class DenseCloudData
{
    public List<string> ImageIds { get; set; } = [];
    public List<DenseSample> Samples { get; set; } = [];
    public int InputSamples
    {
        get; set;
    }
}
