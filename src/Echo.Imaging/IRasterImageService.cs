using SkiaSharp;

namespace Echo.Imaging;

public interface IRasterImageService
{
    RasterPixels ReadPixels(string path);
    // Keep the bitmap alive and unmodified until the borrowed span has been consumed.
    ReadOnlySpan<SKColor> BorrowPixels(SKBitmap bitmap, out bool swapRedBlue);
    GrayscaleImage ResizeGray(RasterPixels source, int width);
    GrayscaleImage ResizeGray(SKBitmap source, int width);
    GrayscaleImage ResizeGray(SKBitmap source, int width, float[] destination);
    RasterDimensions ResizeDimensions(int sourceWidth, int sourceHeight, int width);
    SKBitmap Decode(string path);
    SKBitmap Resize(SKBitmap source, int width);
    RasterPixels ResizePixels(SKBitmap source, int width);
    string? Save(SKBitmap bitmap, string path, SKEncodedImageFormat format, int quality = 92);
    float[] Gray(SKBitmap bitmap);
    float Sample(float[] pixels, int width, int height, double x, double y);
}
