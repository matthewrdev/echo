using Echo.Meshing.Volumes;
using Echo.Models;
using Echo.Storage.Pages;
using Echo.Storage.Records;
using Echo.Storage.Resources;
using Echo.Storage;

namespace Echo.Meshing.Repair;

internal static class VolumeGapRepair
{
    public static IReadOnlyDictionary<Grid, float> Complete(FusedVolume volume, CancellationToken cancellation, WorkBuffers? work = null, Action<string>? progress = null)
    {
        var samples = new BinaryHashIndex<Grid, float>(volume.Samples.Count, new GridRecordCodec(), new FloatRecordCodec(), work, cancellation);
        foreach (var sample in volume.Samples)
        {
            cancellation.ThrowIfCancellationRequested();
            samples.Add(new Grid(sample.X, sample.Y, sample.Z), sample.Distance);
        }

        progress?.Invoke($"Indexed {samples.Count:N0} volume samples; finding bounded gaps…");
        using var scratch = work?.CreateScope();
        BinaryHashIndex<Grid, int>? candidates = null;
        foreach (var key in samples.Keys)
        {
            cancellation.ThrowIfCancellationRequested();
            for (var axis = 0; axis < 3; axis++)
            {
                var x = axis == 0 ? 1 : 0;
                var y = axis == 1 ? 1 : 0;
                var z = axis == 2 ? 1 : 0;
                if (samples.ContainsKey(key.Add(x, y, z)))
                {
                    continue;
                }

                for (var span = 2; span <= 8; span++)
                {
                    if (!samples.ContainsKey(key.Add(x * span, y * span, z * span)))
                    {
                        continue;
                    }

                    for (var step = 1; step < span; step++)
                    {
                        candidates ??= new BinaryHashIndex<Grid, int>(Math.Min(volume.Samples.Count, 4096),
                            new GridRecordCodec(), new IntRecordCodec(), scratch, cancellation);
                        candidates.TryAdd(key.Add(x * step, y * step, z * step), 0);
                    }

                    break;
                }
            }
        }

        if (candidates == null)
        {
            return samples;
        }
        // Interpolate bounded gaps bracketed on two independent axes. Read only
        // the original field, so completion cannot recursively expand a scan.
        // Agreement between the two interpolants rejects discontinuities while
        // allowing a locally planar ground surface to span sparse observations.
        IList<VolumeGapSample> repaired = scratch?.List(new VolumeGapSampleCodec()) ?? new List<VolumeGapSample>();
        foreach (var key in candidates.Keys)
        {
            cancellation.ThrowIfCancellationRequested();
            var count = 0;
            double sum = 0;
            double weight = 0;
            var minimum = double.PositiveInfinity;
            var maximum = double.NegativeInfinity;
            for (var axis = 0; axis < 3; axis++)
            {
                var x = axis == 0 ? 1 : 0;
                var y = axis == 1 ? 1 : 0;
                var z = axis == 2 ? 1 : 0;
                var beforeStep = Bracket(samples, key, -x, -y, -z, out var before);
                var afterStep = Bracket(samples, key, x, y, z, out var after);
                var span = beforeStep + afterStep;
                var bracketed = beforeStep > 0 && afterStep > 0 && span <= 8;
                if (!bracketed)
                {
                    continue;
                }

                var value = ((double)before * afterStep + (double)after * beforeStep) / span;
                if (!double.IsFinite(value) || Math.Abs(before - after) > volume.VoxelSize * span * 2)
                {
                    count = 0;
                    break;
                }

                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
                var confidence = 1.0 / (span * span);
                sum += value * confidence;
                weight += confidence;
                count++;
            }

            if (count >= 2 && maximum - minimum <= volume.VoxelSize * .5)
            {
                repaired.Add(new VolumeGapSample(key, (float)(sum / weight)));
            }
        }

        foreach (var entry in repaired)
        {
            samples.Add(entry.Key, entry.Value);
        }

        return samples;
    }

    private static int Bracket(IReadOnlyDictionary<Grid, float> samples, Grid key, int x, int y, int z, out float value)
    {
        for (var step = 1; step < 8; step++)
        {
            if (samples.TryGetValue(key.Add(x * step, y * step, z * step), out value))
            {
                return step;
            }
        }

        value = 0;
        return 0;
    }
}
