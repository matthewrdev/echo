using SkiaSharp;

namespace Echo.Rendering.Previews;

internal delegate bool PointPreviewProjection(double[] position, out SKPoint pixel, out float depth);
