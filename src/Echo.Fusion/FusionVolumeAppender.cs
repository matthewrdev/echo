using Echo.Contracts;
using Echo.Storage;
using Echo.Geometry;

namespace Echo.Fusion;

internal sealed class FusionVolumeAppender
{
    private readonly IList<VolumeSample> output;
    private readonly FusionPreviewSampler? preview;
    private readonly RegionCropFilter crop;
    private readonly double voxelSize;
    private readonly VolumeSample[] buffer = new VolumeSample[4096];

    public FusionVolumeAppender(IList<VolumeSample> output, FusionPreviewSampler? preview,
        IReadOnlyList<ExclusionRegion> regions, double voxelSize)
    {
        this.output = output;
        this.preview = preview;
        crop = new RegionCropFilter(regions);
        this.voxelSize = voxelSize;
    }

    public void Append(IEnumerable<VolumeSample> samples)
    {
        if (!crop.HasRegions && samples is ArraySegment<VolumeSample> segment)
        {
            Write(segment.AsSpan());
            return;
        }
        var count = 0;
        foreach (var sample in samples)
        {
            if (crop.IsExcluded(new V3(sample.X * voxelSize, sample.Y * voxelSize, sample.Z * voxelSize)))
            {
                continue;
            }
            buffer[count++] = sample;
            if (count == buffer.Length)
            {
                Write(buffer);
                count = 0;
            }
        }
        Write(buffer.AsSpan(0, count));
    }

    private void Write(ReadOnlySpan<VolumeSample> samples)
    {
        if (output is PagedList<VolumeSample> paged)
        {
            paged.AddRange(samples);
        }
        else
        {
            foreach (var sample in samples)
            {
                output.Add(sample);
            }
        }
        preview?.AddRange(samples);
    }
}
