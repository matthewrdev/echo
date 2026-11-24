using SkiaSharp;

namespace Echo.Rendering.Previews;

public interface IPreviewPngWriter
{
    T Write<T>(string output, int width, int height, Func<SKCanvas, T> draw);
}
