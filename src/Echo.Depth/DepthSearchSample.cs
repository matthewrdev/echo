using Echo.Geometry;

namespace Echo.Depth;

public sealed class DepthSearchSample
{
    public int PixelIndex
    {
        get; init;
    }
    public V3 Ray
    {
        get; init;
    }
    public double[] Patch { get; init; } = [];
    public double Norm
    {
        get; init;
    }
    public double[] Hypotheses { get; set; } = [];
}
