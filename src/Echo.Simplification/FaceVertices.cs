namespace Echo.Simplification;

internal readonly record struct FaceVertices(int A, int B, int C)
{
    public bool Contains(int vertex) => A == vertex || B == vertex || C == vertex;

    public int this[int corner] => corner switch
    {
        0 => A,
        1 => B,
        2 => C,
        _ => throw new ArgumentOutOfRangeException(nameof(corner))
    };

    public FaceVertices Replace(int from, int to) => new(A == from ? to : A, B == from ? to : B, C == from ? to : C);
}
