using Echo.Contracts;
using System.Buffers;
using Echo.Compute;
using Echo.Geometry;

namespace Echo.Texturing;

internal static class SeamDiffusionCompute
{
    public static V3[]? TryDiffuse(IComputeService? compute, ComputeBackend backend, List<TextureSeamNode> nodes,
        V3[] local, CancellationToken cancellation)
    {
        var minimumSize = backend == ComputeBackend.Metal ? 1024 : 16384;
        if (compute == null || local.Length < minimumSize)
        {
            return null;
        }
        if (!compute.CanAllocate((long)nodes.Count * 36 + nodes.Sum(node => (long)node.Neighbours.Count * sizeof(int)) + 4, backend))
        {
            return null;
        }
        using var session = compute.Open(backend, cancellation);
        if (session == null)
        {
            return null;
        }
        var values = ArrayPool<float>.Shared.Rent(local.Length * 4);
        try
        {
            var offsets = new int[nodes.Count + 1];
            for (var i = 0; i < nodes.Count; i++)
            {
                offsets[i + 1] = checked(offsets[i] + nodes[i].Neighbours.Count);
                values[i * 4] = (float)local[i].X;
                values[i * 4 + 1] = (float)local[i].Y;
                values[i * 4 + 2] = (float)local[i].Z;
                values[i * 4 + 3] = nodes[i].Weight > 0 ? 1 : 0;
            }
            var neighbours = new int[offsets[^1]];
            for (var i = 0; i < nodes.Count; i++)
            {
                nodes[i].Neighbours.CopyTo(neighbours, offsets[i]);
            }
            session.Upload<int>(0, offsets);
            session.Upload<int>(1, neighbours);
            session.Upload<float>(2, values.AsSpan(0, local.Length * 4));
            session.Allocate(3, checked(local.Length * 4 * sizeof(float)));
            for (var pass = 0; pass < 32; pass++)
            {
                cancellation.ThrowIfCancellationRequested();
                session.Dispatch(ComputeKernel.SeamDiffuse, local.Length, [pass % 2]);
            }
            session.Read<float>(2, values.AsSpan(0, local.Length * 4));
            var result = new V3[local.Length];
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = new V3(values[i * 4], values[i * 4 + 1], values[i * 4 + 2]);
            }
            return result;
        }
        catch (ComputeException) when (backend == ComputeBackend.Auto)
        {
            return null;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(values);
        }
    }
}
