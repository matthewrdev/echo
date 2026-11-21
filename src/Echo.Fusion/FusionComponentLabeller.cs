using Echo.Models;
using Echo.Storage.Pages;
using Echo.Storage.Records;
using Echo.Storage.Resources;
using Echo.Storage;

namespace Echo.Fusion;

public static class FusionComponentLabeller
{
    public static FusionComponentLabels Build(IList<VolumeSample> samples, CancellationToken cancellation,
        WorkBuffers? work, Action<string>? progress)
    {
        var lookup = new BinaryHashIndex<Grid, int>(samples.Count, new GridRecordCodec(), new IntRecordCodec(), work, cancellation);
        IList<int> parents = work?.Filled(samples.Count, 0, new IntRecordCodec(), cancellation) ?? new int[samples.Count];
        for (var index = 0; index < samples.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var sample = samples[index];
            lookup.Add(new Grid(sample.X, sample.Y, sample.Z), index);
            parents[index] = index;
        }

        for (var index = 0; index < samples.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var sample = samples[index];
            // Each of the 26 undirected neighbours is visited once, rather than
            // from both ends. No self lookup or flood-fill queue is necessary.
            for (var z = -1; z <= 0; z++)
            {
                for (var y = -1; y <= (z == 0 ? 0 : 1); y++)
                {
                    for (var x = -1; x <= (z == 0 && y == 0 ? -1 : 1); x++)
                    {
                        if (lookup.TryGetValue(new Grid(sample.X + x, sample.Y + y, sample.Z + z), out var neighbour))
                        {
                            var left = Root(parents, index);
                            var right = Root(parents, neighbour);
                            // Minimum input index preserves the old seed ordering,
                            // including the winner when two largest islands tie.
                            if (left != right)
                            {
                                parents[Math.Max(left, right)] = Math.Min(left, right);
                            }
                        }
                    }
                }
            }
            if (index % 50000 == 0)
            {
                progress?.Invoke($"Linking fusion regions: {index:N0}/{samples.Count:N0} samples");
            }
        }

        IList<int> sizes = work?.Filled(samples.Count, 0, new IntRecordCodec(), cancellation) ?? new int[samples.Count];
        for (var index = 0; index < samples.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var root = Root(parents, index);
            parents[index] = root;
            sizes[root]++;
        }
        var largest = 0;
        var regions = 0;
        for (var index = 0; index < sizes.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (sizes[index] > 0)
            {
                regions++;
            }
            if (sizes[index] > sizes[largest])
            {
                largest = index;
            }
        }
        return new FusionComponentLabels { Labels = parents, Sizes = sizes, Largest = largest, RegionCount = regions };
    }

    private static int Root(IList<int> parents, int index)
    {
        while (parents[index] != index)
        {
            parents[index] = parents[parents[index]];
            index = parents[index];
        }
        return index;
    }
}
