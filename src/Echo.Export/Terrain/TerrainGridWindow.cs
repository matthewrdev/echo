namespace Echo.Export.Terrain;

internal readonly record struct TerrainGridWindow(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left + 1;
    public int Height => Bottom - Top + 1;

    public bool Overlaps(TerrainGridWindow other) =>
        Left <= other.Right && Right >= other.Left && Top <= other.Bottom && Bottom >= other.Top;

    public IEnumerable<int> Boundary(int columns)
    {
        for (var column = Left; column < Right; column++)
        {
            yield return Top * columns + column;
        }

        for (var row = Top; row < Bottom; row++)
        {
            yield return row * columns + Right;
        }

        for (var column = Right; column > Left; column--)
        {
            yield return Bottom * columns + column;
        }

        for (var row = Bottom; row > Top; row--)
        {
            yield return row * columns + Left;
        }
    }
}
