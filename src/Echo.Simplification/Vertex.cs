using Echo.Geometry;

namespace Echo.Simplification;

internal struct Vertex
{
    private Quadric quadric;

    public int Id
    {
        get; init;
    }
    public V3 Position
    {
        get; set;
    }
    public double this[int coefficient]
    {
        readonly get => quadric[SymmetricIndex(coefficient)];
        set => quadric[SymmetricIndex(coefficient)] = value;
    }
    private readonly int SymmetricIndex(int coefficient)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(coefficient);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(coefficient, 15);
        var row = coefficient / 4;
        var column = coefficient % 4;
        var first = Math.Min(row, column);
        var second = Math.Max(row, column);
        return first * 4 - first * (first + 1) / 2 + second;
    }

    public int PlaneCount
    {
        get; set;
    }
    public int Revision
    {
        get; set;
    }
    public bool Removed
    {
        get; set;
    }
}
