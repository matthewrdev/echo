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
        var byteColour = source.ColorType is SKColorType.Bgra8888 or SKColorType.Rgba8888;
        var unpremultiplied = source.AlphaType is SKAlphaType.Opaque or SKAlphaType.Unpremul;
        var packedPixels = BitConverter.IsLittleEndian && source.RowBytes == source.Width * sizeof(uint);
        var nativeLayout = byteColour && packedPixels;
        if (nativeLayout && unpremultiplied)
        {
            // SKColor is an unpremultiplied ARGB uint. Borrow native pixels for
            // this call; RGBA sources swap the accumulated red and blue channels.
            // No full-resolution managed pixel copy is made.
            var pixels = MemoryMarshal.Cast<byte, SKColor>(source.GetPixelSpan());
            return ResizeGray(pixels, source.Width, source.Height, width, source.ColorType == SKColorType.Rgba8888);
        }

        // Skia converts other formats, padded rows and premultiplied alpha to
        // the same colours as the existing bitmap-based resizing path.
        return ResizeGray(source.Pixels, source.Width, source.Height, width);
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
                long red = 0;
                long green = 0;
                long blue = 0;
                var count = (x1 - x0) * (y1 - y0);
                for (var sy = y0; sy < y1; sy++)
                {
                    var row = pixels.Slice(sy * sourceWidth + x0, x1 - x0);
                    foreach (var color in row)
                    {
                        red += color.Red;
                        green += color.Green;
                        blue += color.Blue;
                    }
                }

                // Keep the same byte rounding as Resize followed by Gray.
                var redChannel = (byte)((swapRedBlue ? blue : red) / count);
                var blueChannel = (byte)((swapRedBlue ? red : blue) / count);
                output[y * targetWidth + x] = (.299f * redChannel
                    + .587f * (byte)(green / count) + .114f * blueChannel) / 255;
            }
        }

        return new GrayscaleImage(targetWidth, targetHeight, output);
    }

    public SKBitmap Decode(string path)
    {
        using var codec = SKCodec.Create(path) ?? throw new EchoException($"Cannot decode image: {path}");
        using var raw = SKBitmap.Decode(codec) ?? throw new EchoException($"Cannot decode image: {path}");
        var orientation = (int)codec.EncodedOrigin;
        if (orientation == 1)
        {
            return raw.Copy();
        }

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

    public SKBitmap Resize(SKBitmap source, int width)
    {
        var scale = Math.Min(1.0, (double)width / source.Width);
        var bitmap = new SKBitmap(Math.Max(1, (int)Math.Round(source.Width * scale)), Math.Max(1, (int)Math.Round(source.Height * scale)));
        // Area averaging prevents aliasing when a full-resolution photograph becomes a
        // small stereo image. Point sampling destroys correspondence at these scale ratios.
        var input = source.Pixels;
        var output = new SKColor[bitmap.Width * bitmap.Height];
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var x0 = x * source.Width / bitmap.Width;
                var x1 = (x + 1) * source.Width / bitmap.Width;
                var y0 = y * source.Height / bitmap.Height;
                var y1 = (y + 1) * source.Height / bitmap.Height;
                long red = 0;
                long green = 0;
                long blue = 0;
                var count = (x1 - x0) * (y1 - y0);
                for (var sy = y0; sy < y1; sy++)
                {
                    for (var sx = x0; sx < x1; sx++)
                    {
                        var color = input[sy * source.Width + sx];
                        red += color.Red;
                        green += color.Green;
                        blue += color.Blue;
                    }
                }

                output[y * bitmap.Width + x] = new SKColor((byte)(red / count), (byte)(green / count), (byte)(blue / count));
            }
        }

        bitmap.Pixels = output;
        return bitmap;
    }

    public void Save(SKBitmap bitmap, string path, SKEncodedImageFormat format, int quality = 92)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality);
        fileOperations.AtomicBytes(path, data.ToArray());
    }

    public float[] Gray(SKBitmap bitmap)
    {
        var pixels = bitmap.Pixels;
        var result = new float[pixels.Length];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = (.299f * pixels[i].Red + .587f * pixels[i].Green + .114f * pixels[i].Blue) / 255;
        }

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
