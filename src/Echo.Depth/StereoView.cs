using Echo.Geometry;

namespace Echo.Depth;

internal sealed class StereoView
{
    public V3 Centre
    {
        get; init;
    }
    public double Focal
    {
        get; init;
    }
    public double[] WorldToCamera { get; init; } = [];
    public V3 PatchRight
    {
        get; init;
    }
    public V3 PatchDown
    {
        get; init;
    }
    public float[] Gray { get; init; } = [];
    public int Width
    {
        get; init;
    }
    public int Height
    {
        get; init;
    }
}
