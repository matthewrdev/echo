using System.Buffers;
using Echo.Compute;
using Echo.Contracts;
using Echo.Geometry;

namespace Echo.Fusion;

internal static class FusionCompute
{
    public static IList<VolumeSample> Integrate(IComputeSession session, IList<Grid> keys, int first, int count,
        double voxel, V3 origin, int viewCount, double tolerance, CancellationToken cancellation)
    {
        var jobs = ArrayPool<float>.Shared.Rent(count * 4);
        var values = ArrayPool<float>.Shared.Rent(count * 2);
        try
        {
            for (var i = 0; i < count; i++)
            {
                var key = keys[first + i];
                jobs[i * 4] = (float)(key.X * voxel - origin.X);
                jobs[i * 4 + 1] = (float)(key.Y * voxel - origin.Y);
                jobs[i * 4 + 2] = (float)(key.Z * voxel - origin.Z);
                jobs[i * 4 + 3] = 0;
            }
            session.Upload<float>(2, jobs.AsSpan(0, count * 4));
            session.Allocate(3, checked(count * 2 * sizeof(float)));
            session.Upload<float>(4, [(float)tolerance, (float)voxel]);
            session.Dispatch(ComputeKernel.FusionIntegrate, count, [viewCount]);
            session.Read<float>(3, values.AsSpan(0, count * 2));
            var result = new List<VolumeSample>(count);
            for (var i = 0; i < count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                if (values[i * 2 + 1] < 2)
                {
                    continue;
                }
                var key = keys[first + i];
                result.Add(new VolumeSample { X = key.X, Y = key.Y, Z = key.Z, Distance = values[i * 2], Views = (int)values[i * 2 + 1] });
            }
            return result;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(jobs);
            ArrayPool<float>.Shared.Return(values);
        }
    }
}
