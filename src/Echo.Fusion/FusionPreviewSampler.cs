using Echo.Contracts;

namespace Echo.Fusion;

internal sealed class FusionPreviewSampler
{
    private readonly List<VolumeSample> samples = [];
    private readonly int limit;
    private long observed;
    private long stride = 1;

    public FusionPreviewSampler(int limit = 80000)
    {
        this.limit = limit;
    }

    public void Add(VolumeSample sample)
    {
        var index = observed++;
        if (index % stride != 0)
        {
            return;
        }
        if (samples.Count == limit)
        {
            for (var target = 0; target < samples.Count / 2; target++)
            {
                samples[target] = samples[target * 2];
            }
            samples.RemoveRange(samples.Count / 2, samples.Count - samples.Count / 2);
            stride *= 2;
        }
        if (index % stride == 0)
        {
            samples.Add(sample);
        }
    }

    public FusionPreview Snapshot(FusedVolume volume) => new()
    {
        TotalSamples = volume.Samples.Count,
        Volume = new FusedVolume
        {
            VoxelSize = volume.VoxelSize,
            InputSamples = volume.InputSamples,
            ConsistentSamples = volume.ConsistentSamples,
            Resolution = volume.Resolution,
            Samples = samples.ToList()
        }
    };
}
