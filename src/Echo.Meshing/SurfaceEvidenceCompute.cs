using System.Buffers;
using Echo.Compute;
using Echo.Contracts;
using Echo.Geometry;

namespace Echo.Meshing;

internal static class SurfaceEvidenceCompute
{
    public static void Evaluate(IComputeSession session, Surface surface, int start, int count,
        V3 origin, int views, double tolerance, IList<bool> rejected, CancellationToken cancellation)
    {
        var triangles = ArrayPool<float>.Shared.Rent(count * 12);
        var values = ArrayPool<int>.Shared.Rent(count);
        try
        {
            for (var i = 0; i < count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                var face = surface.Faces[start + i];
                for (var corner = 0; corner < 3; corner++)
                {
                    var vertex = surface.Vertices[face[corner]];
                    var slot = i * 12 + corner * 4;
                    triangles[slot] = (float)(vertex[0] - origin.X);
                    triangles[slot + 1] = (float)(vertex[1] - origin.Y);
                    triangles[slot + 2] = (float)(vertex[2] - origin.Z);
                    triangles[slot + 3] = 0;
                }
            }
            session.Upload<float>(2, triangles.AsSpan(0, count * 12));
            session.Allocate(3, count * sizeof(int));
            session.Upload<float>(4, [(float)tolerance]);
            session.Dispatch("surface_visibility", count, [views]);
            session.Read<int>(3, values.AsSpan(0, count));
            for (var i = 0; i < count; i++)
            {
                rejected[start + i] = values[i] != 0;
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(triangles);
            ArrayPool<int>.Shared.Return(values);
        }
    }
}
