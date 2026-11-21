using Echo.Models;
using Echo.Storage.Records;
using Echo.Storage.Resources;
using Echo.Storage;

namespace Echo.Fusion;

public static class FusionIslandTrimmer
{
    public static FusionIslandTrimResult Trim(FusedVolume volume, IslandTrimMode mode, double minimumSizePercent, CancellationToken cancellation, WorkBuffers? work = null, Action<string>? progress = null)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
        if (!double.IsFinite(minimumSizePercent) || minimumSizePercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumSizePercent));
        }
        if (mode == IslandTrimMode.Off)
        {
            return new FusionIslandTrimResult { Volume = volume };
        }
        if (volume.Samples.Count == 0)
        {
            return new FusionIslandTrimResult { Volume = volume, InputRegions = 0 };
        }

        using var scratch = work?.CreateScope(preferResident: true);
        var components = FusionComponentLabeller.Build(volume.Samples, cancellation, scratch, progress);
        var labels = components.Labels;
        var sizes = components.Sizes;
        var largest = components.Largest;
        var threshold = sizes[largest] * minimumSizePercent / 100;
        bool Remove(int label) => label != largest && (mode == IslandTrimMode.Main || sizes[label] < threshold);
        var removedRegions = Enumerable.Range(0, sizes.Count).Count(label => sizes[label] > 0 && Remove(label));
        if (removedRegions == 0)
        {
            return new FusionIslandTrimResult { Volume = volume, InputRegions = components.RegionCount };
        }
        var result = new FusedVolume
        {
            Samples = work?.List(new VolumeRecordCodec()) ?? new List<VolumeSample>(),
            VoxelSize = volume.VoxelSize,
            InputSamples = volume.InputSamples,
            ConsistentSamples = volume.ConsistentSamples,
            Resolution = volume.Resolution
        };
        for (var index = 0; index < labels.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!Remove(labels[index]))
            {
                result.Samples.Add(volume.Samples[index]);
            }
        }
        return new FusionIslandTrimResult
        {
            Volume = result,
            InputRegions = components.RegionCount,
            RemovedRegions = removedRegions,
            RemovedSamples = volume.Samples.Count - result.Samples.Count
        };
    }
}
