namespace Echo.Meshing.Geometry;

internal readonly record struct SurfaceTriangleVertices(
    int A,
    int B,
    int C)
{
    public int this[int corner] => corner switch
    {
        0 => A,
        1 => B,
        2 => C,
        _ => throw new ArgumentOutOfRangeException(nameof(corner))
    };
}
