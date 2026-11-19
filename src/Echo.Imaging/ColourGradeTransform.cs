using Echo.Contracts;
using SkiaSharp;
using System.Runtime.InteropServices;

namespace Echo.Imaging;

// Version 1 is mirrored by the native CPU histogram and Metal preview.
public sealed class ColourGradeTransform
{
    private readonly bool identity;
    private readonly double redGain;
    private readonly double greenGain;
    private readonly double blueGain;
    private readonly double black;
    private readonly double range;
    private readonly double contrast;
    private readonly double shadows;
    private readonly double highlights;
    private readonly double saturation;
    private readonly double vibrance;

    public ColourGradeTransform(ColourGrade? grade)
    {
        grade ??= new ColourGrade();
        grade.Validate();
        identity = grade.IsIdentity;
        redGain = Math.Pow(2, grade.Exposure + grade.Temperature * .5 + grade.Tint * .25);
        greenGain = Math.Pow(2, grade.Exposure - grade.Tint * .25);
        blueGain = Math.Pow(2, grade.Exposure - grade.Temperature * .5 + grade.Tint * .25);
        black = grade.BlackPoint;
        range = grade.WhitePoint - grade.BlackPoint;
        contrast = grade.Contrast;
        shadows = grade.Shadows;
        highlights = grade.Highlights;
        saturation = grade.Saturation;
        vibrance = grade.Vibrance;
    }

    public SKColor Apply(SKColor colour)
    {
        if (identity)
        {
            return colour;
        }
        var red = Tone(colour.Red / 255.0, redGain);
        var green = Tone(colour.Green / 255.0, greenGain);
        var blue = Tone(colour.Blue / 255.0, blueGain);
        var light = .2126 * red + .7152 * green + .0722 * blue;
        var lift = 4 * light * (1 - light) * .25 * (shadows * (1 - light) + highlights * light);
        var chroma = Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue));
        var gain = saturation * (1 + vibrance * (1 - chroma));
        return new SKColor(Channel(light + lift + (red - light) * gain),
            Channel(light + lift + (green - light) * gain), Channel(light + lift + (blue - light) * gain), colour.Alpha);
    }

    public void Apply(SKBitmap bitmap, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (identity)
        {
            return;
        }
        var byteColour = bitmap.ColorType is SKColorType.Bgra8888 or SKColorType.Rgba8888;
        if (!BitConverter.IsLittleEndian || !byteColour)
        {
            throw new ArgumentException("Grading requires an RGBA/BGRA byte image.", nameof(bitmap));
        }
        var swap = bitmap.ColorType == SKColorType.Rgba8888;
        Parallel.For(0, bitmap.Height, new ParallelOptions
        {
            CancellationToken = cancellation,
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2)
        }, row =>
        {
            var pixels = MemoryMarshal.Cast<byte, SKColor>(bitmap.GetPixelSpan().Slice(row * bitmap.RowBytes, bitmap.Width * 4));
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
                var premultiplied = bitmap.AlphaType == SKAlphaType.Premul && colour.Alpha < 255;
                if (premultiplied)
                {
                    colour = new SKColor(Channel(colour.Red / (double)colour.Alpha),
                        Channel(colour.Green / (double)colour.Alpha), Channel(colour.Blue / (double)colour.Alpha), colour.Alpha);
                }
                var graded = Apply(colour);
                if (premultiplied)
                {
                    graded = new SKColor(Channel(graded.Red / 255.0 * graded.Alpha / 255),
                        Channel(graded.Green / 255.0 * graded.Alpha / 255), Channel(graded.Blue / 255.0 * graded.Alpha / 255), graded.Alpha);
                }
                pixels[column] = swap ? new SKColor(graded.Blue, graded.Green, graded.Red, graded.Alpha) : graded;
            }
        });
    }

    private double Tone(double encoded, double gain)
    {
        var linear = encoded <= .04045 ? encoded / 12.92 : Math.Pow((encoded + .055) / 1.055, 2.4);
        linear *= gain;
        var exposed = linear <= .0031308 ? linear * 12.92 : 1.055 * Math.Pow(linear, 1 / 2.4) - .055;
        return Math.Clamp(((exposed - black) / range - .5) * contrast + .5, 0, 1);
    }

    private byte Channel(double value) => (byte)Math.Floor(Math.Clamp(value, 0, 1) * 255 + .5);
}
