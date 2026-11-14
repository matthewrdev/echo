using System.Runtime.CompilerServices;

namespace Echo.Imaging;

public readonly ref struct GrayscaleSampler
{
    private readonly ReadOnlySpan<float> pixels;
    private readonly int width;
    private readonly int height;

    public GrayscaleSampler(ReadOnlySpan<float> pixels, int width, int height)
    {
        this.pixels = pixels;
        this.width = width;
        this.height = height;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Sample(double x, double y)
    {
        var outsideHorizontal = x < 0 || x >= width - 1;
        var outsideVertical = y < 0 || y >= height - 1;
        if (outsideHorizontal || outsideVertical)
        {
            return float.NaN;
        }

        var ix = (int)x;
        var iy = (int)y;
        var dx = (float)(x - ix);
        var dy = (float)(y - iy);
        var top = iy * width + ix;
        var bottom = (iy + 1) * width + ix;
        // Keep the original float multiply/add order: changing it can change
        // which side of a stereo rejection threshold a hypothesis falls on.
        return pixels[top] * (1 - dx) * (1 - dy) + pixels[top + 1] * dx * (1 - dy)
            + pixels[bottom] * (1 - dx) * dy + pixels[bottom + 1] * dx * dy;
    }
}
