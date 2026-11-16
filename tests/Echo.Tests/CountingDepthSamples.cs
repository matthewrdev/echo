using Echo.Contracts;

namespace Echo.Tests;

internal sealed class CountingDepthSamples : IDepthSamples
{
    private readonly float[] depths;

    public CountingDepthSamples(float[] depths)
    {
        this.depths = depths;
    }

    public int Count => depths.Length;
    public long ReadSamples
    {
        get; private set;
    }
    public float DepthAt(int index) => depths[index];
    public float ConfidenceAt(int index) => 0;

    public void ReadDepth(int start, Span<float> destination)
    {
        ReadSamples += destination.Length;
        depths.AsSpan(start, destination.Length).CopyTo(destination);
    }
}
