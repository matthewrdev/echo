using System.Runtime.InteropServices;
using System.Text;
using Echo.Models;
using Echo.Infrastructure;
using SkiaSharp;

namespace Echo.Imaging;

public sealed class RasterImageService : IRasterImageService
{
    private readonly IFileOperations fileOperations;

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
        return ResizeGrayNative(source, width, null);
    }

    public GrayscaleImage ResizeGray(SKBitmap source, int width, float[] destination)
    {
        return ResizeGrayNative(source, width, destination);
    }

    private GrayscaleImage ResizeGrayNative(SKBitmap source, int width, float[]? destination)
    {
        var pixels = BorrowPixels(source, out var swapRedBlue);
        var result = ResizeGray(pixels, source.Width, source.Height, width, swapRedBlue, destination);
        GC.KeepAlive(source);
        return result;
    }

    public ReadOnlySpan<SKColor> BorrowPixels(SKBitmap source, out bool swapRedBlue)
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

    public RasterDimensions ResizeDimensions(int sourceWidth, int sourceHeight, int width)
    {
        var scale = Math.Min(1.0, (double)width / sourceWidth);
        var targetWidth = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        return new RasterDimensions(targetWidth, targetHeight);
    }

    private GrayscaleImage ResizeGray(ReadOnlySpan<SKColor> pixels, int sourceWidth, int sourceHeight, int width,
        bool swapRedBlue = false, float[]? destination = null)
    {
        var size = ResizeDimensions(sourceWidth, sourceHeight, width);
        var targetWidth = size.Width;
        var targetHeight = size.Height;
        var length = checked(targetWidth * targetHeight);
        var output = destination ?? new float[length];
        if (output.Length < length)
        {
            throw new ArgumentException("Grayscale destination is smaller than the resized image.", nameof(destination));
        }

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
        if (Path.GetExtension(path).ToLowerInvariant() is ".heic" or ".heif")
        {
            return DecodeHeif(path);
        }

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

    private SKBitmap DecodeHeif(string path)
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new EchoException("HEIC and HEIF import requires macOS.", 2);
        }

        var pathBytes = Encoding.UTF8.GetBytes(Path.GetFullPath(path));
        using var url = MacImageIo.CFURLCreateFromFileSystemRepresentation(0, pathBytes, pathBytes.Length, 0);
        if (url.IsInvalid)
        {
            throw new EchoException($"Cannot open HEIF image: {path}", 2);
        }

        using var source = MacImageIo.CGImageSourceCreateWithURL(url, 0);
        if (source.IsInvalid)
        {
            throw new EchoException($"Cannot open HEIF image: {path}", 2);
        }

        // Image I/O applies the primary item's orientation at full resolution when no size limit is set.
        using var options = MacImageIo.CreateOrientedImageOptions();
        if (options.IsInvalid)
        {
            throw new EchoException("Cannot configure the macOS HEIF decoder.", 2);
        }

        var index = MacImageIo.CGImageSourceGetPrimaryImageIndex(source);
        using var image = MacImageIo.CGImageSourceCreateThumbnailAtIndex(source, index, options);
        if (image.IsInvalid || !MacImageIo.HasDecodedPixels(image))
        {
            throw new EchoException($"Cannot decode HEIF image: {path}", 2);
        }

        var width = checked((int)MacImageIo.CGImageGetWidth(image));
        var height = checked((int)MacImageIo.CGImageGetHeight(image));
        var bitmap = new SKBitmap();
        try
        {
            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
            if (!bitmap.TryAllocPixels(info))
            {
                throw new EchoException($"Cannot allocate pixels for HEIF image: {path}", 2);
            }

            using var colourSpace = MacImageIo.CreateSrgbColourSpace();
            const uint alphaNoneSkipLast = 5;
            using var context = MacImageIo.CGBitmapContextCreate(bitmap.GetPixels(), (nuint)width, (nuint)height,
                8, (nuint)bitmap.RowBytes, colourSpace, alphaNoneSkipLast);
            if (context.IsInvalid)
            {
                throw new EchoException($"Cannot render HEIF image: {path}", 2);
            }

            MacImageIo.CGContextDrawImage(context, new MacImageRect(0, 0, width, height), image);
            bitmap.NotifyPixelsChanged();
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
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
        var size = ResizeDimensions(source.Width, source.Height, width);
        var targetWidth = size.Width;
        var targetHeight = size.Height;
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
        return new GrayscaleSampler(pixels, width, height).Sample(x, y);
    }
}
