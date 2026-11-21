using System.Buffers;
using Echo.Compute;
using Echo.Models;

namespace Echo.Geometry;

public static class TriangulationCompute
{
    public static Triangulated?[]? TryCalculate(IComputeService? compute, ComputeBackend backend, Photo left, Photo right,
        CameraPose a, CameraPose b, IReadOnlyList<Match> matches, double tolerance, CancellationToken cancellation)
    {
        // Staging plus double verification was slower than the reference in
        // measured pairs. Retain this path for explicit Metal comparisons only.
        var eligible = backend == ComputeBackend.Metal && matches.Count >= 512;
        if (compute == null || !eligible)
        {
            return null;
        }

        if (!compute.CanAllocate((long)matches.Count * 8 * sizeof(float) + 48 * sizeof(float), backend))
        {
            return null;
        }

        using var session = compute.Open(backend, cancellation);
        if (session == null)
        {
            return null;
        }

        var packed = ArrayPool<float>.Shared.Rent(matches.Count * 4);
        var values = ArrayPool<float>.Shared.Rent(matches.Count * 4);
        try
        {
            var origin = new V3(a.Centre);
            Span<float> views = stackalloc float[48];
            Pack(left, a, origin, views[..24]);
            Pack(right, b, origin, views[24..]);
            for (var i = 0; i < matches.Count; i++)
            {
                packed[i * 4] = (float)matches[i].A[0];
                packed[i * 4 + 1] = (float)matches[i].A[1];
                packed[i * 4 + 2] = (float)matches[i].B[0];
                packed[i * 4 + 3] = (float)matches[i].B[1];
            }

            session.Upload<float>(0, views);
            session.Upload<float>(1, packed.AsSpan(0, matches.Count * 4));
            session.Allocate(2, checked(matches.Count * 4 * sizeof(float)));
            session.Dispatch(ComputeKernel.TriangulatePairs, matches.Count, [matches.Count]);
            session.Read<float>(2, values.AsSpan(0, matches.Count * 4));
            var result = new Triangulated?[matches.Count];
            for (var i = 0; i < matches.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                var error = values[i * 4 + 3];
                var needsReference = !float.IsFinite(error) || error < 0 || error >= tolerance * .95;
                if (needsReference)
                {
                    result[i] = CameraGeometry.Triangulate(left, right, a, b, matches[i], tolerance);
                    continue;
                }

                var point = origin + new V3(values[i * 4], values[i * 4 + 1], values[i * 4 + 2]);
                var pa = CameraGeometry.ProjectPoint(left, a, point);
                var pb = CameraGeometry.ProjectPoint(right, b, point);
                if (!pa.HasValue || !pb.HasValue)
                {
                    result[i] = CameraGeometry.Triangulate(left, right, a, b, matches[i], tolerance);
                    continue;
                }

                var verified = Math.Sqrt((CameraGeometry.Square(pa.Value.X - matches[i].A[0])
                    + CameraGeometry.Square(pa.Value.Y - matches[i].A[1])
                    + CameraGeometry.Square(pb.Value.X - matches[i].B[0])
                    + CameraGeometry.Square(pb.Value.Y - matches[i].B[1])) * .5);
                result[i] = verified <= tolerance ? new Triangulated { Point = point, Error = verified }
                    : CameraGeometry.Triangulate(left, right, a, b, matches[i], tolerance);
            }

            return result;
        }
        catch (ComputeException) when (backend == ComputeBackend.Auto)
        {
            return null;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(packed);
            ArrayPool<float>.Shared.Return(values);
        }
    }

    private static void Pack(Photo photo, CameraPose camera, V3 origin, Span<float> values)
    {
        values.Clear();
        var centre = new V3(camera.Centre) - origin;
        values[0] = (float)centre.X;
        values[1] = (float)centre.Y;
        values[2] = (float)centre.Z;
        values[3] = (float)CameraGeometry.Focal(photo, camera);
        var inverse = CameraGeometry.Transpose(camera.Rotation);
        for (var i = 0; i < 9; i++)
        {
            values[i + 4] = (float)inverse[i];
        }

        values[13] = photo.Width;
        values[14] = photo.Height;
    }
}
