namespace Echo.Geometry;

public readonly record struct V3(double X, double Y, double Z)
{
    public V3(double[] values) : this(values[0], values[1], values[2])
    {
    }

    public double[] Array => [X, Y, Z];
    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
    public V3 Unit => this / Math.Max(Length, 1e-15);

    public static V3 operator +(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static V3 operator -(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static V3 operator *(V3 a, double b) => new(a.X * b, a.Y * b, a.Z * b);
    public static V3 operator /(V3 a, double b) => a * (1 / b);
    public double Dot(V3 other) => X * other.X + Y * other.Y + Z * other.Z;
    public V3 Cross(V3 other) => new(Y * other.Z - Z * other.Y, Z * other.X - X * other.Z, X * other.Y - Y * other.X);
}
