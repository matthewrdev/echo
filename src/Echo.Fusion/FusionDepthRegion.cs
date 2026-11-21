namespace Echo.Fusion;

internal readonly record struct FusionDepthRegion(
    int Left,
    int Top,
    int Width,
    int Height)
{
    public int Count => checked(Width * Height);
}
