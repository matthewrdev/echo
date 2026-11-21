using Echo.Models;

namespace Echo.Storage.Matching;

internal static class FeatureDescriptorPacking
{
    public static PackedFeatureDescriptors Pack(FeatureSet features)
    {
        var count = checked(features.Features.Sum(feature => feature.DescriptorData.Length)
            + features.Lines.Sum(line => line.DescriptorData.Length));
        var result = new PackedFeatureDescriptors(new float[count], new int[features.Features.Count], new int[features.Lines.Count]);
        var offset = Pack(features.Features, result.Values, result.PointOffsets, 0);
        Pack(features.Lines, result.Values, result.LineOffsets, offset);
        return result;
    }

    private static int Pack<T>(IReadOnlyList<T> features, float[] values, int[] offsets, int start) where T : IFeatureDescriptor
    {
        // Matching partitions by descriptor length while preserving original feature order.
        // Persist each such partition contiguously so it can be uploaded without repacking.
        foreach (var length in features.Select(feature => feature.DescriptorData.Length).Distinct())
        {
            for (var index = 0; index < features.Count; index++)
            {
                var data = features[index].DescriptorData;
                if (data.Length != length)
                {
                    continue;
                }
                offsets[index] = start;
                data.Span.CopyTo(values.AsSpan(start));
                start += length;
            }
        }
        return start;
    }
}
