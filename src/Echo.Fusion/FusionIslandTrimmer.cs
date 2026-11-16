using Echo.Contracts;
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

        IDictionary<Grid, int> lookup = work?.Map(new GridRecordCodec(), new IntRecordCodec()) ?? new Dictionary<Grid, int>(volume.Samples.Count);
        for (var index = 0; index < volume.Samples.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var sample = volume.Samples[index];
            lookup.Add(new Grid(sample.X, sample.Y, sample.Z), index);
        }
        IList<int> labels = work?.Filled(volume.Samples.Count, -1, new IntRecordCodec()) ?? Enumerable.Repeat(-1, volume.Samples.Count).ToArray();
        IList<int> sizes = work?.List(new IntRecordCodec()) ?? new List<int>();
        IList<int> pending = work?.List(new IntRecordCodec()) ?? new List<int>();
        for (var seed = 0; seed < labels.Count; seed++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (labels[seed] >= 0)
            {
                continue;
            }
            var label = sizes.Count;
            var count = 0;
            labels[seed] = label;
            pending.Clear();
            pending.Add(seed);
            for (var next = 0; next < pending.Count; next++)
            {
                var index = pending[next];
                cancellation.ThrowIfCancellationRequested();
                count++;
                if (count % 50000 == 0)
                {
                    progress?.Invoke($"Tracing fusion island {label + 1:N0}: {count:N0} samples");
                }
                var sample = volume.Samples[index];
                // Corner neighbours belong to the same observed region too.
                // Removing only whole 26-connected regions preserves every
                // extraction cell and signed distance of the retained region.
                for (var z = -1; z <= 1; z++)
                {
                    for (var y = -1; y <= 1; y++)
                    {
                        for (var x = -1; x <= 1; x++)
                        {
                            var key = new Grid(sample.X + x, sample.Y + y, sample.Z + z);
                            if (lookup.TryGetValue(key, out var other) && labels[other] < 0)
                            {
                                labels[other] = label;
                                pending.Add(other);
                            }
                        }
                    }
                }
            }
            sizes.Add(count);
        }

        var largest = 0;
        for (var index = 1; index < sizes.Count; index++)
        {
            if (sizes[index] > sizes[largest])
            {
                largest = index;
            }
        }
        var threshold = sizes[largest] * minimumSizePercent / 100;
        bool Remove(int label) => label != largest && (mode == IslandTrimMode.Main || sizes[label] < threshold);
        var removedRegions = Enumerable.Range(0, sizes.Count).Count(Remove);
        if (removedRegions == 0)
        {
            return new FusionIslandTrimResult { Volume = volume, InputRegions = sizes.Count };
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
            InputRegions = sizes.Count,
            RemovedRegions = removedRegions,
            RemovedSamples = volume.Samples.Count - result.Samples.Count
        };
    }
}
