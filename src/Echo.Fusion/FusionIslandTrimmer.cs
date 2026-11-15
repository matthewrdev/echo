using Echo.Contracts;

namespace Echo.Fusion;

public static class FusionIslandTrimmer
{
    public static FusionIslandTrimResult Trim(FusedVolume volume, IslandTrimMode mode, double minimumSizePercent, CancellationToken cancellation)
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

        var lookup = new Dictionary<Grid, int>(volume.Samples.Count);
        for (var index = 0; index < volume.Samples.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var sample = volume.Samples[index];
            lookup.Add(new Grid(sample.X, sample.Y, sample.Z), index);
        }
        var labels = new int[volume.Samples.Count];
        Array.Fill(labels, -1);
        var sizes = new List<int>();
        var pending = new Queue<int>();
        for (var seed = 0; seed < labels.Length; seed++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (labels[seed] >= 0)
            {
                continue;
            }
            var label = sizes.Count;
            var count = 0;
            labels[seed] = label;
            pending.Enqueue(seed);
            while (pending.TryDequeue(out var index))
            {
                cancellation.ThrowIfCancellationRequested();
                count++;
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
                                pending.Enqueue(other);
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
        var remove = sizes.Select((size, index) => index != largest && (mode == IslandTrimMode.Main || size < threshold)).ToArray();
        if (!remove.Any(value => value))
        {
            return new FusionIslandTrimResult { Volume = volume, InputRegions = sizes.Count };
        }
        var result = new FusedVolume
        {
            VoxelSize = volume.VoxelSize,
            InputSamples = volume.InputSamples,
            ConsistentSamples = volume.ConsistentSamples,
            Resolution = volume.Resolution
        };
        for (var index = 0; index < labels.Length; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!remove[labels[index]])
            {
                result.Samples.Add(volume.Samples[index]);
            }
        }
        return new FusionIslandTrimResult
        {
            Volume = result,
            InputRegions = sizes.Count,
            RemovedRegions = remove.Count(value => value),
            RemovedSamples = volume.Samples.Count - result.Samples.Count
        };
    }
}
