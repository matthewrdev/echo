using Echo.Storage;
using Echo.Fusion;

namespace Echo.Benchmarks;

internal sealed record FusionPlanningMeasurement(int Points, int Workers, int BatchCapacity, GridAxis Axis, double Voxel,
    long GridSamples, double Seconds, double CpuSeconds, long AllocatedBytes, long WorkingSetBytes,
    PagingStatistics Statistics);
