using SkiaSharp;

namespace Echo.Texturing.Layout;

internal sealed record TexturePatchPlacement(
    SKRect Bounds,
    int QuarterTurns);
