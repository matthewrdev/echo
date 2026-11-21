using SkiaSharp;
using Echo.Models;

namespace Echo.Texturing.Rasterization;

internal static class TextureBackground
{
    public static void Fill(Span<SKColor> pixels, int size, CancellationToken cancellation, Action<OperationProgress>? progress = null)
    {
        if (size == 1)
        {
            if (pixels[0].Alpha == 0)
            {
                pixels[0] = new SKColor(48, 52, 58);
            }

            return;
        }

        progress?.Invoke(new OperationProgress("Atlas background", $"Building colour continuation at {size:N0} × {size:N0}…"));
        var coarseSize = (size + 1) / 2;
        var coarse = new SKColor[coarseSize * coarseSize];
        for (var y = 0; y < coarseSize; y++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (var x = 0; x < coarseSize; x++)
            {
                var red = 0;
                var green = 0;
                var blue = 0;
                var count = 0;
                for (var dy = 0; dy < 2 && y * 2 + dy < size; dy++)
                {
                    for (var dx = 0; dx < 2 && x * 2 + dx < size; dx++)
                    {
                        var colour = pixels[(y * 2 + dy) * size + x * 2 + dx];
                        if (colour.Alpha == 0)
                        {
                            continue;
                        }

                        red += colour.Red;
                        green += colour.Green;
                        blue += colour.Blue;
                        count++;
                    }
                }

                if (count > 0)
                {
                    coarse[y * coarseSize + x] = new SKColor((byte)(red / count), (byte)(green / count), (byte)(blue / count));
                }
            }
        }

        // Push observed colours into a pyramid, then pull a smooth continuation
        // back into unused atlas space. Photographic texels and their exact
        // nearest-colour gutters are never blurred or overwritten.
        Fill(coarse, coarseSize, cancellation, progress);
        for (var y = 0; y < size; y++)
        {
            if (y % 128 == 0)
            {
                progress?.Invoke(new OperationProgress("Atlas background", $"Completing colour continuation at {size:N0} × {size:N0}…", y, size, "rows"));
            }

            cancellation.ThrowIfCancellationRequested();
            for (var x = 0; x < size; x++)
            {
                var index = y * size + x;
                if (pixels[index].Alpha != 0)
                {
                    continue;
                }

                pixels[index] = Interpolate(coarse, coarseSize, x * .5 - .25, y * .5 - .25);
            }
        }
    }

    private static SKColor Interpolate(SKColor[] pixels, int size, double x, double y)
    {
        x = Math.Clamp(x, 0, size - 1);
        y = Math.Clamp(y, 0, size - 1);
        var left = (int)x;
        var top = (int)y;
        var right = Math.Min(left + 1, size - 1);
        var bottom = Math.Min(top + 1, size - 1);
        var dx = x - left;
        var dy = y - top;
        var a = pixels[top * size + left];
        var b = pixels[top * size + right];
        var c = pixels[bottom * size + left];
        var d = pixels[bottom * size + right];
        byte Blend(byte aa, byte bb, byte cc, byte dd) => (byte)(aa * (1 - dx) * (1 - dy)
            + bb * dx * (1 - dy) + cc * (1 - dx) * dy + dd * dx * dy);
        return new SKColor(Blend(a.Red, b.Red, c.Red, d.Red), Blend(a.Green, b.Green, c.Green, d.Green), Blend(a.Blue, b.Blue, c.Blue, d.Blue));
    }
}
