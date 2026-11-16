namespace Echo.Fusion;

// An inclusive, contiguous Z interval at one X/Y coordinate. This preserves
// every cell while avoiding per-cell records for long uncertainty rays.
internal readonly record struct GridSpan(int X, int Y, int FirstZ, int LastZ) : IComparable<GridSpan>
{
    public long CellCount => (long)LastZ - FirstZ + 1;

    public int CompareTo(GridSpan other)
    {
        var order = X.CompareTo(other.X);
        if (order == 0)
        {
            order = Y.CompareTo(other.Y);
        }
        return order != 0 ? order : FirstZ.CompareTo(other.FirstZ);
    }

    public bool Joins(GridSpan other)
    {
        var sameColumn = X == other.X && Y == other.Y;
        return sameColumn && other.FirstZ <= (long)LastZ + 1;
    }

    public GridSpan Union(GridSpan other) => this with { LastZ = Math.Max(LastZ, other.LastZ) };
}
