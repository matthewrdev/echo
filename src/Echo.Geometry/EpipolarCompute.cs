using System.Buffers;
using Echo.Compute;
using Echo.Contracts;

namespace Echo.Geometry;

internal static class EpipolarCompute
{
    public static IComputeSession? Open(IComputeService? compute, ComputeBackend backend, IReadOnlyList<Match> matches, CancellationToken cancellation)
    {
        var minimumSize = backend == ComputeBackend.Metal ? 1024 : 16384;
        if (compute == null || matches.Count < minimumSize)
        {
            return null;
        }
        var session = compute.Open(backend, cancellation);
        if (session == null)
        {
            return null;
        }
        var packed = ArrayPool<float>.Shared.Rent(matches.Count * 4);
        try
        {
            for (var i = 0; i < matches.Count; i++)
            {
                packed[i * 4] = (float)matches[i].A[0];
                packed[i * 4 + 1] = (float)matches[i].A[1];
                packed[i * 4 + 2] = (float)matches[i].B[0];
                packed[i * 4 + 3] = (float)matches[i].B[1];
            }
            session.Upload<float>(0, packed.AsSpan(0, matches.Count * 4));
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
            ArrayPool<float>.Shared.Return(packed);
        }
    }

    public static void Count(IComputeSession session, double[]?[] matrices, int count, int matches, double threshold, Span<int> result)
    {
        Span<float> values = stackalloc float[count * 9];
        for (var i = 0; i < count; i++)
        {
            for (var j = 0; j < 9; j++)
            {
                values[i * 9 + j] = (float)(matrices[i]?[j] ?? 0);
            }
        }
        session.Upload<float>(1, values);
        session.Allocate(2, checked(count * matches * sizeof(int)));
        session.Allocate(3, count * sizeof(int));
        Span<int> parameters = stackalloc int[] { matches, count, BitConverter.SingleToInt32Bits((float)threshold) };
        session.Dispatch("epipolar_candidates", checked(count * matches), parameters);
        session.Dispatch("epipolar_counts", count * 256, parameters);
        session.Read<int>(3, result[..count]);
    }
}
