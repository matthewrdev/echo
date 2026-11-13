using Echo.Contracts;
using Echo.Geometry;

namespace Echo.Depth;

internal sealed class StereoView
{
    public Photo Photo { get; init; } = new();
    public CameraPose Camera { get; init; } = new();
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
    public double Focal => (Camera.FocalPixels > 0 ? Camera.FocalPixels : Photo.FocalPixels) * Width / Photo.Width;
}
