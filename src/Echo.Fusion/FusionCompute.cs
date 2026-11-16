using System.Buffers;
using Echo.Compute;
using Echo.Contracts;
using Echo.Geometry;

namespace Echo.Fusion;

internal sealed class FusionCompute : IDisposable
{
    private readonly IComputeSession session;
    private readonly float[] jobs;
    private readonly float[] values;
    private readonly float[] metadata = new float[128 * 24];
    private bool first;
    private bool disposed;

    public FusionCompute(IComputeSession session, FusionIntegrationPlan plan)
    {
        this.session = session;
        session.Allocate(0, metadata.Length * sizeof(float));
        session.Allocate(1, checked(plan.DepthCapacity * sizeof(float)));
        session.Allocate(2, checked(plan.BatchCapacity * 4 * sizeof(float)));
        session.Allocate(3, checked(plan.BatchCapacity * 4 * sizeof(float)));
        session.Allocate(4, 2 * sizeof(float));
        jobs = ArrayPool<float>.Shared.Rent(plan.BatchCapacity * 4);
        try
        {
            values = ArrayPool<float>.Shared.Rent(plan.BatchCapacity * 4);
        }
        catch
        {
            ArrayPool<float>.Shared.Return(jobs);
            throw;
        }
    }

    public void Begin(ReadOnlySpan<Grid> cells, double voxel, V3 origin, double tolerance)
    {
        first = true;
        for (var index = 0; index < cells.Length; index++)
        {
            var key = cells[index];
            jobs[index * 4] = (float)(key.X * voxel - origin.X);
            jobs[index * 4 + 1] = (float)(key.Y * voxel - origin.Y);
            jobs[index * 4 + 2] = (float)(key.Z * voxel - origin.Z);
            jobs[index * 4 + 3] = 0;
        }
        session.Upload<float>(2, jobs.AsSpan(0, cells.Length * 4));
        session.Upload<float>(4, [(float)tolerance, (float)voxel]);
    }

    public void Add(FusionDepthTiles tiles, V3 origin, int count)
    {
        for (var index = 0; index < tiles.Count; index++)
        {
            var window = tiles.Windows[index];
            var view = window.View;
            var data = metadata.AsSpan(index * 24, 24);
            data.Clear();
            data[0] = (float)(view.Centre.X - origin.X);
            data[1] = (float)(view.Centre.Y - origin.Y);
            data[2] = (float)(view.Centre.Z - origin.Z);
            data[3] = (float)view.Focal;
            for (var component = 0; component < 9; component++)
            {
                data[4 + component] = (float)view.Inverse[component];
            }
            data[13] = view.Map.Width;
            data[14] = view.Map.Height;
            data[15] = BitConverter.Int32BitsToSingle(window.Offset);
            data[16] = window.Region.Left;
            data[17] = window.Region.Top;
            data[18] = window.Region.Width;
            data[19] = window.Region.Height;
        }
        session.Upload<float>(0, metadata.AsSpan(0, tiles.Count * 24));
        session.Upload<float>(1, tiles.Depths.AsSpan(0, tiles.Length));
        session.Dispatch(ComputeKernel.FusionIntegrate, count, [tiles.Count, first ? 1 : 0]);
        first = false;
    }

    public int Finish(ReadOnlySpan<Grid> cells, Span<VolumeSample> output, CancellationToken cancellation)
    {
        if (first)
        {
            return 0;
        }
        session.Read<float>(3, values.AsSpan(0, cells.Length * 4));
        var retained = 0;
        for (var index = 0; index < cells.Length; index++)
        {
            if (index % 4096 == 0)
            {
                cancellation.ThrowIfCancellationRequested();
            }
            var support = (int)values[index * 4 + 2];
            if (support >= 2)
            {
                var key = cells[index];
                output[retained++] = new VolumeSample
                {
                    X = key.X,
                    Y = key.Y,
                    Z = key.Z,
                    Distance = values[index * 4] / values[index * 4 + 1],
                    Views = support
                };
            }
        }
        return retained;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        ArrayPool<float>.Shared.Return(jobs);
        ArrayPool<float>.Shared.Return(values);
    }
}
