using System.Buffers;
using Echo.Compute;
using Echo.Contracts;

namespace Echo.Depth;

internal static class DepthFilterCompute
{
    public static int? TryFilter(IComputeService? compute, ComputeBackend backend, DepthMap map, CancellationToken cancellation)
    {
        var useCpu = map.Depth.Length < 16384 || backend == ComputeBackend.Cpu;
        if (compute == null || useCpu)
        {
            return null;
        }
        if (!compute.CanAllocate((long)map.Depth.Length * 4 * sizeof(float), backend))
        {
            return null;
        }
        var count = map.Depth.Length;
        var depth = ArrayPool<float>.Shared.Rent(count);
        var confidence = ArrayPool<float>.Shared.Rent(count);
        var retained = ArrayPool<int>.Shared.Rent(count);
        try
        {
            using var session = compute.Open(backend, cancellation);
            if (session == null)
            {
                return null;
            }
            session.Upload<float>(0, map.Depth);
            session.Upload<float>(1, map.Depth);
            session.Upload<float>(2, map.Confidence);
            session.Allocate(3, checked(count * sizeof(int)));
            session.Dispatch(ComputeKernel.DepthFilter, count, [map.Width, map.Height]);
            session.Read<float>(1, depth.AsSpan(0, count));
            session.Read<float>(2, confidence.AsSpan(0, count));
            session.Read<int>(3, retained.AsSpan(0, count));
            cancellation.ThrowIfCancellationRequested();
            depth.AsSpan(0, count).CopyTo(map.Depth);
            confidence.AsSpan(0, count).CopyTo(map.Confidence);
            var total = 0;
            for (var i = 0; i < count; i++)
            {
                total += retained[i];
            }
            return total;
        }
        catch (ComputeException) when (backend == ComputeBackend.Auto)
        {
            return null;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(depth);
            ArrayPool<float>.Shared.Return(confidence);
            ArrayPool<int>.Shared.Return(retained);
        }
    }
}
