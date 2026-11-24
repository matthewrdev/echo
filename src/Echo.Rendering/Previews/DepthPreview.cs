using Echo.Models;
using SkiaSharp;

namespace Echo.Rendering.Previews;

public static class DepthPreview
{
    public static void Draw(SKCanvas canvas, SKRect area, DepthMap map, bool confidence)
    {
        if (map.Width < 1 || map.Height < 1 || (long)map.Width * map.Height != map.SampleCount)
        {
            throw new EchoException("The saved depth map has invalid dimensions.", 2);
        }

        var bounds = PreviewImage.Fit(map.Width, map.Height, area);
        var width = Math.Max(1, (int)Math.Round(bounds.Width));
        var height = Math.Max(1, (int)Math.Round(bounds.Height));
        using var image = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var pixels = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, SKColor>(image.GetPixelSpan());
        var range = confidence ? default : FindRange(map);
        for (var y = 0; y < height; y++)
        {
            var row = (int)((long)y * map.Height / height) * map.Width;
            for (var x = 0; x < width; x++)
            {
                var index = row + (int)((long)x * map.Width / width);
                var depth = map.DepthAt(index);
                var value = confidence ? map.ConfidenceAt(index) : range.Normalize(depth);
                var valid = depth > 0 && float.IsFinite(depth) && float.IsFinite(value);
                pixels[y * width + x] = valid ? Colour(Math.Clamp(value, 0, 1), confidence) : PreviewImage.Background();
            }
        }

        canvas.DrawBitmap(image, bounds);
    }

    private static DepthRange FindRange(DepthMap map)
    {
        var count = Math.Min(65536, map.SampleCount);
        var values = new List<float>(count);
        for (var sample = 0; sample < count; sample++)
        {
            var depth = map.DepthAt((int)((long)sample * map.SampleCount / count));
            if (depth > 0 && float.IsFinite(depth))
            {
                values.Add(depth);
            }
        }

        if (values.Count == 0)
        {
            return default;
        }

        values.Sort();
        return new DepthRange(values[(int)((values.Count - 1) * 0.02)], values[(int)((values.Count - 1) * 0.98)]);
    }

    private static SKColor Colour(float value, bool confidence)
    {
        if (confidence)
        {
            return PreviewImage.Mix(new SKColor(110, 52, 112), new SKColor(193, 255, 123), value);
        }

        var middle = new SKColor(42, 206, 188);
        return value < 0.5f
            ? PreviewImage.Mix(new SKColor(249, 214, 84), middle, value * 2)
            : PreviewImage.Mix(middle, new SKColor(58, 60, 149), (value - 0.5f) * 2);
    }
}
