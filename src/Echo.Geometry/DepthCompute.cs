using Echo.Contracts;
using System.Buffers;
using Echo.Compute;

namespace Echo.Geometry;

public static class DepthCompute
{
    public static IComputeSession? Open(IComputeService? compute, ComputeBackend backend, IReadOnlyList<DepthView> views,
        V3 origin, CancellationToken cancellation)
    {
        if (compute == null || views.Count == 0)
        {
            return null;
        }
        long length = 0;
        foreach (var view in views)
        {
            length += view.Map.SampleCount;
        }
        // Leave room for jobs, output and native allocator overhead. Oversized
        // scenes retain the paged CPU path instead of forcing all maps into RAM.
        if (length > Math.Min(80L * 1024 * 1024, compute.MemoryBudgetBytes / 8))
        {
            return null;
        }
        var session = compute.Open(backend, cancellation);
        if (session == null)
        {
            return null;
        }
        var values = ArrayPool<float>.Shared.Rent(65536);
        try
        {
            session.Allocate(1, checked((int)length * sizeof(float)));
            var metadata = new float[views.Count * 24];
            var offset = 0;
            for (var i = 0; i < views.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                var view = views[i];
                var data = metadata.AsSpan(i * 24, 24);
                data[0] = (float)(view.Centre.X - origin.X);
                data[1] = (float)(view.Centre.Y - origin.Y);
                data[2] = (float)(view.Centre.Z - origin.Z);
                data[3] = (float)view.Focal;
                for (var k = 0; k < 9; k++)
                {
                    data[4 + k] = (float)view.Inverse[k];
                }
                data[13] = view.Map.Width;
                data[14] = view.Map.Height;
                data[15] = BitConverter.Int32BitsToSingle(offset);
                for (var first = 0; first < view.Map.SampleCount; first += values.Length)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var count = Math.Min(values.Length, view.Map.SampleCount - first);
                    if (view.Map.Samples == null)
                    {
                        session.UploadRange<float>(1, offset, view.Map.Depth.AsSpan(first, count));
                    }
                    else
                    {
                        for (var pixel = 0; pixel < count; pixel++)
                        {
                            values[pixel] = view.Map.DepthAt(first + pixel);
                        }
                        session.UploadRange<float>(1, offset, values.AsSpan(0, count));
                    }
                    offset += count;
                }
            }
            session.Upload<float>(0, metadata);
            return session;
        }
        catch (ComputeException) when (backend == ComputeBackend.Auto)
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
            ArrayPool<float>.Shared.Return(values);
        }
    }
}
