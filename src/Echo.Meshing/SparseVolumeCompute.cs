using Echo.Contracts;
using System.Buffers;
using Echo.Compute;
using Echo.Geometry;

namespace Echo.Meshing;

internal static class SparseVolumeCompute
{
    public static IComputeSession? Open(IComputeService? compute, ComputeBackend backend, IReadOnlyDictionary<Grid, float> samples,
        CancellationToken cancellation, out int mask)
    {
        mask = 0;
        var suitableSize = samples.Count >= 4096 && samples.Count <= 4_000_000;
        if (compute == null || !suitableSize)
        {
            return null;
        }
        var capacity = 1;
        while (capacity < samples.Count * 2)
        {
            capacity *= 2;
        }
        if (!compute.CanAllocate((long)capacity * 4 * sizeof(int) + (long)samples.Count * sizeof(float) + 4096 * 12 * sizeof(float), backend))
        {
            return null;
        }
        mask = capacity - 1;
        var session = compute.Open(backend, cancellation);
        if (session == null)
        {
            return null;
        }
        var table = ArrayPool<int>.Shared.Rent(capacity * 4);
        var values = ArrayPool<float>.Shared.Rent(samples.Count);
        try
        {
            table.AsSpan(0, capacity * 4).Clear();
            var index = 0;
            foreach (var entry in samples)
            {
                cancellation.ThrowIfCancellationRequested();
                var slot = Hash(entry.Key) & mask;
                while (table[slot * 4 + 3] != 0)
                {
                    slot = (slot + 1) & mask;
                }
                table[slot * 4] = entry.Key.X;
                table[slot * 4 + 1] = entry.Key.Y;
                table[slot * 4 + 2] = entry.Key.Z;
                table[slot * 4 + 3] = index + 1;
                values[index++] = entry.Value;
            }
            session.Upload<int>(0, table.AsSpan(0, capacity * 4));
            session.Upload<float>(1, values.AsSpan(0, samples.Count));
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
            ArrayPool<int>.Shared.Return(table);
            ArrayPool<float>.Shared.Return(values);
        }
    }

    public static void Read(IComputeSession session, ReadOnlySpan<Grid> keys, Span<float> values, int mask)
    {
        var jobs = ArrayPool<int>.Shared.Rent(keys.Length * 4);
        try
        {
            for (var i = 0; i < keys.Length; i++)
            {
                jobs[i * 4] = keys[i].X;
                jobs[i * 4 + 1] = keys[i].Y;
                jobs[i * 4 + 2] = keys[i].Z;
                jobs[i * 4 + 3] = 0;
            }
            session.Upload<int>(2, jobs.AsSpan(0, keys.Length * 4));
            session.Allocate(3, values.Length * sizeof(float));
            session.Dispatch(ComputeKernel.VolumeCorners, keys.Length, [mask]);
            session.Read<float>(3, values);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(jobs);
        }
    }

    private static int Hash(Grid key)
    {
        var hash = unchecked((uint)key.X * 73856093u ^ (uint)key.Y * 19349663u ^ (uint)key.Z * 83492791u);
        return unchecked((int)(hash ^ (hash >> 16)));
    }
}
