using System.Buffers;
using Echo.Models;
using Echo.Storage.Pages;
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
        var removedRegions = components.RegionSeeds is { } seeds
            ? seeds.Count(Remove)
            : Enumerable.Range(0, sizes.Count).Count(label => sizes[label] > 0 && Remove(label));
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
        var sampleBuffer = ArrayPool<VolumeSample>.Shared.Rent(4096);
        var labelBuffer = ArrayPool<int>.Shared.Rent(4096);
        try
        {
            for (var start = 0; start < labels.Count;)
            {
                cancellation.ThrowIfCancellationRequested();
                var count = Math.Min(4096, labels.Count - start);
                var samples = sampleBuffer.AsSpan(0, count);
                var batchLabels = labelBuffer.AsSpan(0, count);
                RecordListOperations.Read(volume.Samples, start, samples);
                RecordListOperations.Read(labels, start, batchLabels);
                var retained = 0;
                for (var index = 0; index < count; index++)
                {
                    if (!Remove(batchLabels[index]))
                    {
                        samples[retained++] = samples[index];
                    }
                }

                if (result.Samples is PagedList<VolumeSample> paged)
                {
                    paged.AddRange(samples[..retained]);
                }
                else
                {
                    for (var index = 0; index < retained; index++)
                    {
                        result.Samples.Add(samples[index]);
                    }
                }

                start += count;
            }
        }
        finally
        {
            ArrayPool<VolumeSample>.Shared.Return(sampleBuffer);
            ArrayPool<int>.Shared.Return(labelBuffer);
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
