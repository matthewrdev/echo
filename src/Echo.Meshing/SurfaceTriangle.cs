using Echo.Geometry;

namespace Echo.Meshing;

internal readonly record struct SurfaceTriangle(V3 A, V3 B, V3 C)
{
    public V3 Minimum => new(Math.Min(A.X, Math.Min(B.X, C.X)), Math.Min(A.Y, Math.Min(B.Y, C.Y)), Math.Min(A.Z, Math.Min(B.Z, C.Z)));
    public V3 Maximum => new(Math.Max(A.X, Math.Max(B.X, C.X)), Math.Max(A.Y, Math.Max(B.Y, C.Y)), Math.Max(A.Z, Math.Max(B.Z, C.Z)));
}
