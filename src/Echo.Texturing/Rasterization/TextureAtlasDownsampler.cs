using SkiaSharp;

namespace Echo.Texturing.Rasterization;

internal static class TextureAtlasDownsampler
{
    // Exact 2x2 area filtering in linear light, before JPEG compression. Chart
    // padding was reserved on the coarsest atlas grid, so a reduction footprint
    // cannot straddle the painted interiors of unrelated islands.
    public static SKBitmap Half(SKBitmap source, CancellationToken cancellation)
    {
        var byteColour = source.ColorType is SKColorType.Bgra8888 or SKColorType.Rgba8888;
        var packed = source.RowBytes == source.Width * 4;
        var evenDimensions = source.Width % 2 == 0 && source.Height % 2 == 0;
        var supportedPixels = byteColour && packed;
        if (!supportedPixels || !evenDimensions)
        {
            throw new ArgumentException("Expected an even-sized packed RGBA/BGRA atlas.", nameof(source));
        }

        var decode = new double[256];
        for (var value = 0; value < decode.Length; value++)
        {
            var srgb = value / 255.0;
            decode[value] = srgb <= .04045 ? srgb / 12.92 : Math.Pow((srgb + .055) / 1.055, 2.4);
        }

        var encode = new byte[65536];
        for (var value = 0; value < encode.Length; value++)
        {
            var linear = value / 65535.0;
            var srgb = linear <= .0031308 ? linear * 12.92 : 1.055 * Math.Pow(linear, 1 / 2.4) - .055;
            encode[value] = (byte)Math.Clamp((int)Math.Round(srgb * 255), 0, 255);
        }

        var result = new SKBitmap(source.Width / 2, source.Height / 2, source.ColorType, SKAlphaType.Opaque);
        try
        {
            var input = source.GetPixelSpan();
            var output = result.GetPixelSpan();
            for (var y = 0; y < result.Height; y++)
            {
                cancellation.ThrowIfCancellationRequested();
                for (var x = 0; x < result.Width; x++)
                {
                    var top = y * 2 * source.RowBytes + x * 8;
                    var bottom = top + source.RowBytes;
                    var target = y * result.RowBytes + x * 4;
                    for (var channel = 0; channel < 3; channel++)
                    {
                        var average = (decode[input[top + channel]] + decode[input[top + 4 + channel]]
                            + decode[input[bottom + channel]] + decode[input[bottom + 4 + channel]]) * .25;
                        output[target + channel] = encode[Math.Clamp((int)Math.Round(average * 65535), 0, 65535)];
                    }

                    output[target + 3] = 255;
                }
            }

            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }
}
