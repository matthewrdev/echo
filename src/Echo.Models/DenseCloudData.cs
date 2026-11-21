namespace Echo.Models;

public sealed class DenseCloudData
{
    public List<string> ImageIds { get; set; } = [];
    public IList<DenseSample> Samples { get; set; } = [];
    public int InputSamples
    {
        get; set;
    }
}
