using System.Buffers;
using Echo.Compute;
using Echo.Meshing.Geometry;
using Echo.Models;

namespace Echo.Meshing.Visibility;

internal static class SurfaceOcclusionPreparation
{
    public static SurfaceOcclusionCompute? Open(IComputeService? compute, ComputeBackend backend, SurfaceTriangleIndex index,
        CancellationToken cancellation, IComputeSession? sharedSession = null)
    {
        if (backend == ComputeBackend.Auto && index.Order.Count < 4096)
        {
            return null;
        }
        var bytes = checked(index.Nodes.Count * 32L + index.Order.Count * 4L);
        var budget = bytes + SurfaceOcclusionCompute.BatchSize * (SurfaceOcclusionCompute.ResultStride + 8L) * sizeof(int);
        var supported = compute != null && index.Nodes.Count > 0 && bytes <= int.MaxValue;
        if (!supported || !compute!.CanAllocate(budget, backend))
        {
            return null;
        }
        var session = sharedSession ?? compute.Open(backend, cancellation);
        if (session == null)
        {
            return null;
        }
        var origin = (index.Nodes[0].Minimum + index.Nodes[0].Maximum) * .5;
        var staging = ArrayPool<float>.Shared.Rent(32768);
        try
        {
            session.Allocate(7, (int)bytes);
            for (var start = 0; start < index.Nodes.Count; start += staging.Length / 8)
            {
                cancellation.ThrowIfCancellationRequested();
                var count = Math.Min(staging.Length / 8, index.Nodes.Count - start);
                for (var local = 0; local < count; local++)
                {
                    var node = index.Nodes[start + local];
                    var minimum = node.Minimum - origin;
                    var maximum = node.Maximum - origin;
                    var slot = local * 8;
                    staging[slot] = MathF.BitDecrement((float)minimum.X);
                    staging[slot + 1] = MathF.BitDecrement((float)minimum.Y);
                    staging[slot + 2] = MathF.BitDecrement((float)minimum.Z);
                    staging[slot + 3] = BitConverter.Int32BitsToSingle(node.Left < 0 ? ~node.Start : node.Left);
                    staging[slot + 4] = MathF.BitIncrement((float)maximum.X);
                    staging[slot + 5] = MathF.BitIncrement((float)maximum.Y);
                    staging[slot + 6] = MathF.BitIncrement((float)maximum.Z);
                    staging[slot + 7] = BitConverter.Int32BitsToSingle(node.Left < 0 ? node.Count : node.Right);
                }
                session.UploadRange<float>(7, start * 8, staging.AsSpan(0, count * 8));
            }
            for (var start = 0; start < index.Order.Count; start += staging.Length)
            {
                cancellation.ThrowIfCancellationRequested();
                var count = Math.Min(staging.Length, index.Order.Count - start);
                for (var local = 0; local < count; local++)
                {
                    staging[local] = BitConverter.Int32BitsToSingle(index.Order[start + local]);
                }
                session.UploadRange<float>(7, index.Nodes.Count * 8 + start, staging.AsSpan(0, count));
            }
            return new SurfaceOcclusionCompute(session, index, origin, sharedSession == null, backend);
        }
        catch (ComputeException) when (backend == ComputeBackend.Auto)
        {
            if (sharedSession == null)
            {
                session.Dispose();
            }
            return null;
        }
        catch
        {
            if (sharedSession == null)
            {
                session.Dispose();
            }
            throw;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(staging);
        }
    }

}
