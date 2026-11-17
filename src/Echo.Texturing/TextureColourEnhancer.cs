using System.Runtime.InteropServices;
using SkiaSharp;

namespace Echo.Texturing;

public static class TextureColourEnhancer
{
    public static void Apply(SKBitmap atlas, double strength, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!double.IsFinite(strength) || strength is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(strength));
        }
        if (strength == 0)
        {
            return;
        }
        var byteColour = atlas.ColorType is SKColorType.Bgra8888 or SKColorType.Rgba8888;
        if (!BitConverter.IsLittleEndian || !byteColour)
        {
            throw new ArgumentException("Colour enhancement requires an RGBA or BGRA byte atlas.", nameof(atlas));
        }
        var swap = atlas.ColorType == SKColorType.Rgba8888;
        var options = new ParallelOptions
        {
            CancellationToken = cancellation,
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2)
        };
        Parallel.For(0, atlas.Height, options, row =>
        {
            var pixels = MemoryMarshal.Cast<byte, SKColor>(atlas.GetPixelSpan().Slice(row * atlas.RowBytes, atlas.Width * 4));
            for (var column = 0; column < pixels.Length; column++)
            {
                var colour = pixels[column];
                if (colour.Alpha == 0)
                {
                    continue;
                }
                if (swap)
                {
                    colour = new SKColor(colour.Blue, colour.Green, colour.Red, colour.Alpha);
                }
                var enhanced = Enhance(colour, strength);
                pixels[column] = swap
                    ? new SKColor(enhanced.Blue, enhanced.Green, enhanced.Red, enhanced.Alpha) : enhanced;
            }
        });
    }

    private static SKColor Enhance(SKColor colour, double strength)
    {
        // A display-referred finish on the already encoded photographic colour.
        // Apply one global curve to every chart, so grading cannot create seams.
        var red = colour.Red / 255.0;
        var green = colour.Green / 255.0;
        var blue = colour.Blue / 255.0;
        var lightness = .2126 * red + .7152 * green + .0722 * blue;
        var tone = lightness + strength * lightness * (1 - lightness) * (.35 + .45 * (2 * lightness - 1));
        var chroma = Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue));
        var gain = 1 + .25 * strength * (1 - chroma);
        ReadOnlySpan<double> channels = [red, green, blue];
        foreach (var channel in channels)
        {
            var difference = channel - lightness;
            if (difference > 0)
            {
                gain = Math.Min(gain, (1 - tone) / difference);
            }
            else if (difference < 0)
            {
                gain = Math.Min(gain, -tone / difference);
            }
        }
        // Compress chroma together instead of clipping channels independently.
        return new SKColor(Channel(tone + (red - lightness) * gain),
            Channel(tone + (green - lightness) * gain), Channel(tone + (blue - lightness) * gain), colour.Alpha);
    }

    private static byte Channel(double value) => (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);
}
