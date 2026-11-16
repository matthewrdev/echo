using System.Buffers;
using Echo.Compute;
using Echo.Contracts;
using Echo.Geometry;

namespace Echo.Meshing;

internal static class SemanticProjectionCompute
{
    public static IComputeSession? Open(IComputeService? compute, ProjectData project, IReadOnlyList<DepthView> views,
        IReadOnlyDictionary<string, Photo> photos, IReadOnlyDictionary<string, SemanticMap> semantics, V3 origin, CancellationToken cancellation)
    {
        var count = views.Sum(view => (long)semantics[view.Map.ImageId].Width * semantics[view.Map.ImageId].Height);
        if (compute == null || count > Math.Min(32L * 1024 * 1024, compute.MemoryBudgetBytes / 8))
        {
            return null;
        }
        var session = DepthCompute.Open(compute, project.Settings.ComputeBackend, views, origin, cancellation);
        if (session == null)
        {
            return null;
        }
        var pixels = ArrayPool<byte>.Shared.Rent(checked((int)count * 2));
        try
        {
            var metadata = new float[views.Count * 4];
            var offset = 0;
            for (var i = 0; i < views.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                var map = semantics[views[i].Map.ImageId];
                var photo = photos[views[i].Map.ImageId];
                metadata[i * 4] = map.Width;
                metadata[i * 4 + 1] = map.Height;
                metadata[i * 4 + 2] = BitConverter.Int32BitsToSingle(offset / 2);
                metadata[i * 4 + 3] = (float)photo.Width / photo.Height;
                for (var pixel = 0; pixel < map.Width * map.Height; pixel++)
                {
                    pixels[offset++] = map.VegetationSamples[pixel];
                    pixels[offset++] = map.RockSamples[pixel];
                }
            }
            session.Upload<byte>(5, pixels.AsSpan(0, offset));
            session.Upload<float>(6, metadata);
            return session;
        }
        catch (ComputeException) when (project.Settings.ComputeBackend == ComputeBackend.Auto)
        {
            session.Dispose();
            return null;
        }
        catch
        {
            session.Dispose();
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(pixels);
        }
    }

    public static float[] Evaluate(IComputeSession session, Surface surface, IList<V3> normals, int first, int count, V3 origin, int views)
    {
        var points = ArrayPool<float>.Shared.Rent(count * 8);
        try
        {
            for (var i = 0; i < count; i++)
            {
                var point = new V3(surface.Vertices[first + i]) - origin;
                var normal = normals[first + i];
                points[i * 8] = (float)point.X;
                points[i * 8 + 1] = (float)point.Y;
                points[i * 8 + 2] = (float)point.Z;
                points[i * 8 + 3] = 0;
                points[i * 8 + 4] = (float)normal.X;
                points[i * 8 + 5] = (float)normal.Y;
                points[i * 8 + 6] = (float)normal.Z;
                points[i * 8 + 7] = 0;
            }
            var result = new float[checked(count * views * 4)];
            session.Upload<float>(2, points.AsSpan(0, count * 8));
            session.Allocate(3, checked(result.Length * sizeof(float)));
            session.Dispatch(ComputeKernel.SemanticProject, count * views, [views, surface.Faces.Count]);
            session.Read<float>(3, result);
            return result;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(points);
        }
    }
}
