using Echo.Contracts;
using Echo.Geometry;
using Echo.Storage;

namespace Echo.Fusion;

internal static class FusionExclusions
{
    public static DenseCloudData Filter(DenseCloudData cloud, IReadOnlyList<ExclusionRegion> regions,
        CancellationToken cancellation, WorkBuffers? work)
    {
        var crop = new RegionCropFilter(regions);
        if (!crop.HasRegions)
        {
            return cloud;
        }
        var firstExcluded = -1;
        for (var index = 0; index < cloud.Samples.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var sample = cloud.Samples[index];
            if (crop.IsExcluded(new V3(sample.X, sample.Y, sample.Z)))
            {
                firstExcluded = index;
                break;
            }
        }
        // The pipeline's cloud is already cropped. Reuse its paged records
        // instead of copying a potentially multi-gigabyte artifact to scratch.
        if (firstExcluded < 0)
        {
            return cloud;
        }
        var result = new DenseCloudData
        {
            ImageIds = cloud.ImageIds,
            InputSamples = cloud.InputSamples,
            Samples = work?.List(new DenseRecordCodec()) ?? []
        };
        foreach (var sample in cloud.Samples)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!crop.IsExcluded(new V3(sample.X, sample.Y, sample.Z)))
            {
                result.Samples.Add(sample);
            }
        }
        if (result.Samples.Count < 30)
        {
            throw new EchoException("Fewer than 30 dense points remain after cropping. Adjust or disable a crop region before fusion.", 5);
        }
        return result;
    }
}
