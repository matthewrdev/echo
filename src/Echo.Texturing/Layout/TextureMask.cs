namespace Echo.Texturing.Layout;

public sealed record TextureMask(
    int Width,
    int Height,
    TextureMaskSpan[] Spans,
    int QuarterTurns);
