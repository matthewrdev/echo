using Echo.Models;

namespace Echo.Storage.Pages;

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
    public void ReadDepth(int start, Span<float> destination)
    {
        if (depths is PagedList<float> paged)
        {
            paged.ReadRange(start, destination);
            return;
        }

        for (var index = 0; index < destination.Length; index++)
        {
            destination[index] = depths[start + index];
        }
    }
    public float ConfidenceAt(int index) => confidence.Count == 0 ? 0 : confidence[index];
}
