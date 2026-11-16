using Echo.Contracts;

namespace Echo.Geometry;

public sealed class ExcludedDepthSamples : IDepthSamples
{
    private readonly DepthView source;
    private readonly IReadOnlyList<ExclusionRegion> regions;

    public ExcludedDepthSamples(DepthView source, IReadOnlyList<ExclusionRegion> regions)
    {
        this.source = source;
        this.regions = regions;
    }

    public int Count => source.Map.SampleCount;

    public float DepthAt(int index) => Filter(index, source.Map.DepthAt(index));

    public void ReadDepth(int start, Span<float> destination)
    {
        source.Map.ReadDepth(start, destination);
        for (var index = 0; index < destination.Length; index++)
        {
            destination[index] = Filter(start + index, destination[index]);
        }
    }

    public float ConfidenceAt(int index) => DepthAt(index) > 0 ? source.Map.ConfidenceAt(index) : 0;

    private float Filter(int index, float depth)
    {
        if (!(depth > 0) || !float.IsFinite(depth))
        {
            return depth;
        }
        var point = source.Point(index % source.Map.Width, index / source.Map.Width, depth);
        return ExclusionGeometry.Contains(regions, point) ? 0 : depth;
    }
}
