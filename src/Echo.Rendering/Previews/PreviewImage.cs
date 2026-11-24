using Echo.Models;
using SkiaSharp;

namespace Echo.Rendering.Previews;

public static class PreviewImage
{
    public static SKColor Background() => new(20, 24, 31);

    public static void ValidateOutput(string output, int width, int height)
    {
        if (width is < 1 or > 8192 || height is < 1 or > 8192 || (long)width * height > 16_777_216)
        {
            throw new EchoException("Preview dimensions must be 1–8192 pixels per side, with at most 16,777,216 pixels total.", 2);
        }

        if (!string.Equals(Path.GetExtension(output), ".png", StringComparison.OrdinalIgnoreCase) || Path.Exists(output))
        {
            throw new EchoException("Choose a new PNG output path.", 2);
        }
    }

    public static SKRect Fit(float contentWidth, float contentHeight, SKRect area)
    {
        var scale = Math.Min(area.Width / contentWidth, area.Height / contentHeight);
        var halfWidth = contentWidth * scale / 2;
        var halfHeight = contentHeight * scale / 2;
        return new SKRect(area.MidX - halfWidth, area.MidY - halfHeight, area.MidX + halfWidth, area.MidY + halfHeight);
    }

    public static SKColor Mix(SKColor start, SKColor end, float amount)
    {
        return new SKColor(
            (byte)(start.Red + (end.Red - start.Red) * amount),
            (byte)(start.Green + (end.Green - start.Green) * amount),
            (byte)(start.Blue + (end.Blue - start.Blue) * amount));
    }
}
