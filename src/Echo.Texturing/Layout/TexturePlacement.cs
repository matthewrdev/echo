namespace Echo.Texturing.Layout;

internal sealed record TexturePlacement(
    int Index,
    int X,
    int Y,
    int Width,
    int Height,
    bool Rotated);
