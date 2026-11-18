using Echo.Geometry;

namespace Echo.Depth;

public readonly record struct DepthSearchSample
{
    public int PixelIndex
    {
        get; init;
    }
    public V3 Ray
    {
        get; init;
    }
    public ReadOnlyMemory<double> Patch
    {
        get; init;
    }
    public double Norm
    {
        get; init;
    }
    public ReadOnlyMemory<double> Hypotheses
    {
        get; init;
    }
}
