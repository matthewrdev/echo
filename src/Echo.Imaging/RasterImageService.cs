using System.ComponentModel.Composition;
using System.Runtime.InteropServices;
using Echo.Contracts;
using Echo.Infrastructure;
using SkiaSharp;

namespace Echo.Imaging;

[Export(typeof(IRasterImageService)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class RasterImageService : IRasterImageService
{
    private readonly IFileOperations fileOperations;

    [ImportingConstructor]
    public RasterImageService(IFileOperations fileOperations)
    {
        this.fileOperations = fileOperations;
    }

    public RasterPixels ReadPixels(string path)
    {
        using var image = Decode(path);
        return new RasterPixels(image.Width, image.Height, image.Pixels);
    }

    public GrayscaleImage ResizeGray(RasterPixels source, int width)
    {
        return ResizeGray(source.Pixels, source.Width, source.Height, width);
    }

    public GrayscaleImage ResizeGray(SKBitmap source, int width)
    {
        var pixels = BorrowPixels(source, out var swapRedBlue);
        var result = ResizeGray(pixels, source.Width, source.Height, width, swapRedBlue);
        GC.KeepAlive(source);
        return result;
    }

    private ReadOnlySpan<SKColor> BorrowPixels(SKBitmap source, out bool swapRedBlue)
    {
        var byteColour = source.ColorType is SKColorType.Bgra8888 or SKColorType.Rgba8888;
        var unpremultiplied = source.AlphaType is SKAlphaType.Opaque or SKAlphaType.Unpremul;
        var packedPixels = BitConverter.IsLittleEndian && source.RowBytes == source.Width * sizeof(uint);
        var nativeLayout = byteColour && packedPixels;
        if (nativeLayout && unpremultiplied)
        {
            // The caller keeps the bitmap alive until this borrowed span is consumed.
            swapRedBlue = source.ColorType == SKColorType.Rgba8888;
            return MemoryMarshal.Cast<byte, SKColor>(source.GetPixelSpan());
        }
        swapRedBlue = false;
        return source.Pixels;
    }

    private GrayscaleImage ResizeGray(ReadOnlySpan<SKColor> pixels, int sourceWidth, int sourceHeight, int width, bool swapRedBlue = false)
    {
        var scale = Math.Min(1.0, (double)width / sourceWidth);
        var targetWidth = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        var output = new float[targetWidth * targetHeight];
        for (var y = 0; y < targetHeight; y++)
        {
            var y0 = y * sourceHeight / targetHeight;
            var y1 = (y + 1) * sourceHeight / targetHeight;
            for (var x = 0; x < targetWidth; x++)
            {
                var x0 = x * sourceWidth / targetWidth;
                var x1 = (x + 1) * sourceWidth / targetWidth;
                var colour = AveragePixel(pixels, sourceWidth, x0, x1, y0, y1, swapRedBlue);
                output[y * targetWidth + x] = (.299f * colour.Red + .587f * colour.Green + .114f * colour.Blue) / 255;
            }
        }

        return new GrayscaleImage(targetWidth, targetHeight, output);
    }

    public SKBitmap Decode(string path)
    {
        using var codec = SKCodec.Create(path) ?? throw new EchoException($"Cannot decode image: {path}");
        var raw = SKBitmap.Decode(codec) ?? throw new EchoException($"Cannot decode image: {path}");
        var orientation = (int)codec.EncodedOrigin;
        if (orientation == 1)
        {
            return raw;
        }

        using (raw)
        {
            var swapped = orientation >= 5;
            var output = new SKBitmap(swapped ? raw.Height : raw.Width, swapped ? raw.Width : raw.Height);
            for (var y = 0; y < raw.Height; y++)
            {
                for (var x = 0; x < raw.Width; x++)
                {
                    var dx = x;
                    var dy = y;
                    switch (orientation)
                    {
                        case 2:
                            dx = raw.Width - 1 - x;
                            break;
                        case 3:
                            dx = raw.Width - 1 - x;
                            dy = raw.Height - 1 - y;
                            break;
                        case 4:
                            dy = raw.Height - 1 - y;
                            break;
                        case 5:
                            dx = y;
                            dy = x;
                            break;
                        case 6:
                            dx = raw.Height - 1 - y;
                            dy = x;
                            break;
                        case 7:
                            dx = raw.Height - 1 - y;
                            dy = raw.Width - 1 - x;
                            break;
                        case 8:
                            dx = y;
                            dy = raw.Width - 1 - x;
                            break;
                    }

                    output.SetPixel(dx, dy, raw.GetPixel(x, y));
                }
            }

            return output;
        }
    }

    public SKBitmap Resize(SKBitmap source, int width)
    {
        var resized = ResizePixels(source, width);
        var bitmap = new SKBitmap(resized.Width, resized.Height);
        bitmap.Pixels = resized.Pixels;
        return bitmap;
    }

    public RasterPixels ResizePixels(SKBitmap source, int width)
    {
        var scale = Math.Min(1.0, (double)width / source.Width);
        var targetWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
        var input = BorrowPixels(source, out var swapRedBlue);
        var output = new SKColor[targetWidth * targetHeight];
        for (var y = 0; y < targetHeight; y++)
        {
            var y0 = y * source.Height / targetHeight;
            var y1 = (y + 1) * source.Height / targetHeight;
            for (var x = 0; x < targetWidth; x++)
            {
                var x0 = x * source.Width / targetWidth;
                var x1 = (x + 1) * source.Width / targetWidth;
                output[y * targetWidth + x] = AveragePixel(input, source.Width, x0, x1, y0, y1, swapRedBlue);
            }
        }
        GC.KeepAlive(source);
        return new RasterPixels(targetWidth, targetHeight, output);
    }

    private SKColor AveragePixel(ReadOnlySpan<SKColor> pixels, int sourceWidth, int x0, int x1, int y0, int y1, bool swapRedBlue)
    {
        long red = 0;
        long green = 0;
        long blue = 0;
        var count = (x1 - x0) * (y1 - y0);
        for (var y = y0; y < y1; y++)
        {
            var row = pixels.Slice(y * sourceWidth + x0, x1 - x0);
            foreach (var colour in row)
            {
                red += colour.Red;
                green += colour.Green;
                blue += colour.Blue;
            }
        }
        return new SKColor((byte)((swapRedBlue ? blue : red) / count), (byte)(green / count),
            (byte)((swapRedBlue ? red : blue) / count));
    }

    public void Save(SKBitmap bitmap, string path, SKEncodedImageFormat format, int quality = 92)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality);
        fileOperations.AtomicBytes(path, data.ToArray());
    }

    public float[] Gray(SKBitmap bitmap)
    {
        var pixels = BorrowPixels(bitmap, out var swapRedBlue);
        var result = new float[pixels.Length];
        for (var i = 0; i < result.Length; i++)
        {
            var colour = pixels[i];
            var red = swapRedBlue ? colour.Blue : colour.Red;
            var blue = swapRedBlue ? colour.Red : colour.Blue;
            result[i] = (.299f * red + .587f * colour.Green + .114f * blue) / 255;
        }

        GC.KeepAlive(bitmap);
        return result;
    }

    public float Sample(float[] pixels, int width, int height, double x, double y)
    {
        if (x < 0 || y < 0 || x >= width - 1 || y >= height - 1)
        {
            return float.NaN;
        }

        var ix = (int)x;
        var iy = (int)y;
        var dx = (float)(x - ix);
        var dy = (float)(y - iy);
        return pixels[iy * width + ix] * (1 - dx) * (1 - dy) + pixels[iy * width + ix + 1] * dx * (1 - dy) + pixels[(iy + 1) * width + ix] * (1 - dx) * dy + pixels[(iy + 1) * width + ix + 1] * dx * dy;
    }
}
