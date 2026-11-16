using Echo.Contracts;
using Echo.Storage;

namespace Echo.Fusion;

internal sealed class FusionVolumeAppender
{
    private readonly IList<VolumeSample> output;
    private readonly FusionPreviewSampler? preview;
    private readonly VolumeSample[] buffer = new VolumeSample[4096];

    public FusionVolumeAppender(IList<VolumeSample> output, FusionPreviewSampler? preview)
    {
        this.output = output;
        this.preview = preview;
    }

    public void Append(IEnumerable<VolumeSample> samples)
    {
        if (samples is ArraySegment<VolumeSample> segment)
        {
            Write(segment.AsSpan());
            return;
        }
        var count = 0;
        foreach (var sample in samples)
        {
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
        if (preview != null)
        {
            foreach (var sample in samples)
            {
                preview.Add(sample);
            }
        }
    }
}
