using Echo.Models;
using System.Buffers;
using Echo.Compute;
using Echo.Geometry;

namespace Echo.Meshing.Volumes;

internal static class SparseVolumeCompute
{
    public static IComputeSession? Open(IComputeService? compute, ComputeBackend backend, IReadOnlyDictionary<Grid, float> samples,
        CancellationToken cancellation, out int mask, int batchSize = 4096)
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
        if (!addressable || !compute.CanAllocate(tableBytes + sampleBytes + batchSize * 59L * sizeof(int) + ((batchSize + 255L) / 256 + 1) * sizeof(int) + 65536, backend))
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

    public static int ReadActive(IComputeSession session, ReadOnlySpan<Grid> keys, Span<int> records, int mask)
    {
        var jobs = ArrayPool<int>.Shared.Rent(keys.Length * 4);
        try
        {
            for (var cell = 0; cell < keys.Length; cell++)
            {
                jobs[cell * 4] = keys[cell].X;
                jobs[cell * 4 + 1] = keys[cell].Y;
                jobs[cell * 4 + 2] = keys[cell].Z;
                jobs[cell * 4 + 3] = 0;
            }

            session.Upload<int>(2, jobs.AsSpan(0, keys.Length * 4));
            session.Allocate(3, keys.Length * 8 * sizeof(float));
            session.Allocate(4, keys.Length * sizeof(int));
            session.Allocate(5, sizeof(int));
            var groups = (keys.Length + 255) / 256;
            session.Allocate(7, groups * sizeof(int));
            session.Allocate(6, keys.Length * 12 * sizeof(int));
            session.Dispatch(ComputeKernel.VolumeCorners, keys.Length, [mask]);
            session.Dispatch(ComputeKernel.VolumeActiveCells, keys.Length, [keys.Length]);
            session.Dispatch(ComputeKernel.VolumeCompactOffsets, groups, [keys.Length]);
            session.Dispatch(ComputeKernel.VolumeCompactGroups, 1, [groups]);
            session.Dispatch(ComputeKernel.VolumeCompactCells, keys.Length, [keys.Length]);
            Span<int> total = stackalloc int[1];
            session.Read<int>(5, total);
            var count = total[0];
            if ((uint)count > (uint)keys.Length)
            {
                throw new ComputeException("Invalid active-cell count.");
            }

            if (count > 0)
            {
                session.Read<int>(6, records[..checked(count * 12)]);
            }

            return count;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(jobs);
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
