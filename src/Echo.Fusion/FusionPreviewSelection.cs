using Echo.Contracts;

namespace Echo.Fusion;

public static class FusionPreviewSelection
{
    public static List<VolumeSample> Sample(IList<VolumeSample> source)
    {
        var stride = Math.Max(1, (source.Count + 79999L) / 80000);
        var result = new List<VolumeSample>(Math.Min(80000, source.Count));
        for (long index = 0; index < source.Count; index += stride)
        {
            result.Add(source[(int)index]);
        }
        return result;
    }
}
