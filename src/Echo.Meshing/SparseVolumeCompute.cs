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
        if (compute == null || samples.Count < 4096)
        {
            return null;
        }
        // The Metal API uses signed 32-bit buffer lengths. Budget the complete
        // resident index, query batch and bounded host staging before opening it.
        var capacity = 1L;
        while (capacity < samples.Count * 2L)
        {
            capacity *= 2;
        }
        var tableBytes = capacity * sizeof(int);
        var sampleBytes = samples.Count * 4L * sizeof(int);
        var addressable = tableBytes <= int.MaxValue && sampleBytes <= int.MaxValue;
        if (!addressable || !compute.CanAllocate(tableBytes + sampleBytes + 4096 * 12 * sizeof(int) + 65536, backend))
        {
            return null;
        }
        mask = (int)capacity - 1;
        var session = compute.Open(backend, cancellation);
        if (session == null)
        {
            return null;
        }
        var records = ArrayPool<int>.Shared.Rent(16384);
        try
        {
            session.Allocate(0, (int)tableBytes);
            session.Allocate(1, (int)sampleBytes);
            var count = 0;
            var offset = 0;
            foreach (var entry in samples)
            {
                cancellation.ThrowIfCancellationRequested();
                records[count++] = entry.Key.X;
                records[count++] = entry.Key.Y;
                records[count++] = entry.Key.Z;
                records[count++] = BitConverter.SingleToInt32Bits(entry.Value);
                if (count == 16384)
                {
                    session.UploadRange<int>(1, offset, records.AsSpan(0, count));
                    offset += count;
                    count = 0;
                }
            }
            if (count > 0)
            {
                session.UploadRange<int>(1, offset, records.AsSpan(0, count));
            }
            // Separate dispatches establish visibility of initialized slots and
            // the immutable sample records before any corner queries execute.
            session.Dispatch(ComputeKernel.VolumeIndex, (int)capacity, [mask, 0]);
            session.Dispatch(ComputeKernel.VolumeIndex, samples.Count, [mask, 1]);
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
            ArrayPool<int>.Shared.Return(records);
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

}
