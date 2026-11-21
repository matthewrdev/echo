using SkiaSharp;

namespace Echo.Texturing.Rasterization;

internal static class TextureSampling
{
    public static SKColor Bilinear(ReadOnlySpan<SKColor> source, int width, int height, double x, double y, TextureSkyGuard? skyGuard = null)
    {
        if (skyGuard != null)
        {
            var guarded = skyGuard.Adjust(x, y);
            x = guarded.X;
            y = guarded.Y;
        }

        x = Math.Clamp(x, 0, width - 1);
        y = Math.Clamp(y, 0, height - 1);
        var ix = (int)x;
        var iy = (int)y;
        var right = Math.Min(ix + 1, width - 1);
        var bottom = Math.Min(iy + 1, height - 1);
        var dx = x - ix;
        var dy = y - iy;
        var a = source[iy * width + ix];
        var b = source[iy * width + right];
        var c = source[bottom * width + ix];
        var d = source[bottom * width + right];
        byte Blend(byte aa, byte bb, byte cc, byte dd) => (byte)Math.Clamp(
            aa * (1 - dx) * (1 - dy) + bb * dx * (1 - dy) + cc * (1 - dx) * dy + dd * dx * dy, 0, 255);
        return new SKColor(Blend(a.Red, b.Red, c.Red, d.Red), Blend(a.Green, b.Green, c.Green, d.Green), Blend(a.Blue, b.Blue, c.Blue, d.Blue));
    }
}
