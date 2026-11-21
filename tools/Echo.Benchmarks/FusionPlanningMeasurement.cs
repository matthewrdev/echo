using Echo.Fusion;
using Echo.Storage.Pages;
using Echo.Storage;

namespace Echo.Benchmarks;

internal sealed record FusionPlanningMeasurement(int Points, int Workers, int BatchCapacity, GridAxis Axis, double Voxel,
    long GridSamples, double Seconds, double CpuSeconds, long AllocatedBytes, long WorkingSetBytes,
    PagingStatistics Statistics, long PlanningReadBytes, long PlanningWrittenBytes, long PeakWorkingSetBytes, string OutputHash);
