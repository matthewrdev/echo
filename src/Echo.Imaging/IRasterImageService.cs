using SkiaSharp;

namespace Echo.Imaging;

public interface IRasterImageService
{
    RasterPixels ReadPixels(string path);
    GrayscaleImage ResizeGray(RasterPixels source, int width);
    SKBitmap Decode(string path);
    SKBitmap Resize(SKBitmap source, int width);
    void Save(SKBitmap bitmap, string path, SKEncodedImageFormat format, int quality = 92);
    float[] Gray(SKBitmap bitmap);
    float Sample(float[] pixels, int width, int height, double x, double y);
}
