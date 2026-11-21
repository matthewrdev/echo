using System.Collections;
using Echo.Models;

namespace Echo.Geometry;

public sealed class ExcludedDepthSamples : IDepthSamples
{
    private readonly DepthView source;
    private readonly RegionCropFilter crop;
    private BitArray? excluded;

    public ExcludedDepthSamples(DepthView source, IReadOnlyList<ExclusionRegion> regions)
    {
        this.source = source;
        crop = new RegionCropFilter(regions);
    }

    public int Count => source.Map.SampleCount;

    public long MaskBytes => ((Count + 31L) / 32) * sizeof(int);

    public void PrepareMask(CancellationToken cancellation)
    {
        var mask = new BitArray(Count);
        Span<float> depths = stackalloc float[4096];
        for (var start = 0; start < Count;)
        {
            cancellation.ThrowIfCancellationRequested();
            var block = depths[..Math.Min(depths.Length, Count - start)];
            source.Map.ReadDepth(start, block);
            for (var offset = 0; offset < block.Length; offset++)
            {
                if (IsExcluded(start + offset, block[offset]))
                {
                    mask[start + offset] = true;
                }
            }
            start += block.Length;
        }
        excluded = mask;
    }

    public float DepthAt(int index)
    {
        var depth = source.Map.DepthAt(index);
        return (excluded?[index] ?? IsExcluded(index, depth)) ? 0 : depth;
    }

    public void ReadDepth(int start, Span<float> destination)
    {
        source.Map.ReadDepth(start, destination);
        if (excluded is { } mask)
        {
            for (var index = 0; index < destination.Length; index++)
            {
                if (mask[start + index])
                {
                    destination[index] = 0;
                }
            }
            return;
        }

        for (var index = 0; index < destination.Length; index++)
        {
            if (IsExcluded(start + index, destination[index]))
            {
                destination[index] = 0;
            }
        }
    }

    public float ConfidenceAt(int index) => DepthAt(index) > 0 ? source.Map.ConfidenceAt(index) : 0;

    private bool IsExcluded(int index, float depth)
    {
        if (!(depth > 0) || !float.IsFinite(depth))
        {
            return false;
        }
        var point = source.Point(index % source.Map.Width, index / source.Map.Width, depth);
        return crop.IsExcluded(point);
    }
}
