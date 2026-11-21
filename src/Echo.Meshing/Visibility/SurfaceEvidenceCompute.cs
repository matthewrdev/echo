using System.Buffers;
using Echo.Compute;
using Echo.Geometry;
using Echo.Meshing.Geometry;

namespace Echo.Meshing.Visibility;

internal static class SurfaceEvidenceCompute
{
    public static void Evaluate(IComputeSession session, ReadOnlySpan<SurfaceTriangle> geometry,
        V3 origin, int views, double tolerance, Span<bool> rejected, CancellationToken cancellation)
    {
        var count = geometry.Length;
        var triangles = ArrayPool<float>.Shared.Rent(count * 12);
        var values = ArrayPool<int>.Shared.Rent(count);
        try
        {
            for (var i = 0; i < count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                var triangle = geometry[i];
                for (var corner = 0; corner < 3; corner++)
                {
                    var vertex = corner == 0 ? triangle.A : corner == 1 ? triangle.B : triangle.C;
                    var slot = i * 12 + corner * 4;
                    triangles[slot] = (float)(vertex.X - origin.X);
                    triangles[slot + 1] = (float)(vertex.Y - origin.Y);
                    triangles[slot + 2] = (float)(vertex.Z - origin.Z);
                    triangles[slot + 3] = 0;
                }
            }
            session.Upload<float>(2, triangles.AsSpan(0, count * 12));
            session.Allocate(3, count * sizeof(int));
            session.Upload<float>(4, [(float)tolerance]);
            session.Dispatch(ComputeKernel.SurfaceVisibility, count, [views]);
            session.Read<int>(3, values.AsSpan(0, count));
            for (var i = 0; i < count; i++)
            {
                rejected[i] = values[i] != 0;
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(triangles);
            ArrayPool<int>.Shared.Return(values);
        }
    }
}
