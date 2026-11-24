using Echo.Models;
using SkiaSharp;

namespace Echo.Rendering.Previews;

public sealed class PhotoPreview
{
    public PhotoPreview(Photo photo, SKBitmap bitmap, SKRect area)
    {
        Bitmap = bitmap;
        Bounds = PreviewImage.Fit(bitmap.Width, bitmap.Height, area);
        Width = photo.Width > 0 ? photo.Width : bitmap.Width;
        Height = photo.Height > 0 ? photo.Height : bitmap.Height;
    }

    public SKBitmap Bitmap
    {
        get;
    }
    public SKRect Bounds
    {
        get;
    }
    private int Width
    {
        get;
    }
    private int Height
    {
        get;
    }

    public SKPoint Point(double x, double y)
    {
        return new SKPoint(Bounds.Left + (float)(x / Width) * Bounds.Width,
            Bounds.Top + (float)(y / Height) * Bounds.Height);
    }

    public SKPoint Point(double[] point) => Point(point[0], point[1]);

    public void Draw(SKCanvas canvas)
    {
        using var image = SKImage.FromBitmap(Bitmap);
        canvas.DrawImage(image, Bounds, new SKSamplingOptions(SKFilterMode.Linear));
    }
}
