using Echo.Contracts;

namespace Echo.Storage;

public sealed class PagedDepthSamples : IDepthSamples
{
    private readonly IReadOnlyList<float> depths;
    private readonly IReadOnlyList<float> confidence;

    public PagedDepthSamples(IReadOnlyList<float> depths, IReadOnlyList<float> confidence)
    {
        this.depths = depths;
        this.confidence = confidence;
    }

    public int Count => depths.Count;
    public float DepthAt(int index) => depths[index];
    public float ConfidenceAt(int index) => confidence.Count == 0 ? 0 : confidence[index];
}
