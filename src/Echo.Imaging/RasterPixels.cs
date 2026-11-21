using SkiaSharp;

namespace Echo.Imaging;

public sealed record RasterPixels(
    int Width,
    int Height,
    SKColor[] Pixels);
