using Echo.Contracts;
using Echo.Depth;

namespace Echo.Benchmarks;

internal sealed class DepthMeasurement
{
    public required int Iteration
    {
        get; init;
    }
    public required double Seconds
    {
        get; init;
    }
    public required long AllocatedBytes
    {
        get; init;
    }
    public required int CameraCount
    {
        get; init;
    }
    public required int ConcurrentImages
    {
        get; init;
    }
    public required int WorkersPerImage
    {
        get; init;
    }
    public required long EstimatedBytesPerImage
    {
        get; init;
    }
    public required long MemoryBudgetBytes
    {
        get; init;
    }
    public required int DepthMemoryBudgetMb
    {
        get; init;
    }
    public required double WriteSeconds
    {
        get; init;
    }
    public required long WriteAllocatedBytes
    {
        get; init;
    }
    public required List<DepthLevelMetrics>[] Phases
    {
        get; init;
    }
    public required List<ProcessingExecution>[] Executions
    {
        get; init;
    }
    public required int Valid
    {
        get; init;
    }
    public required string Hash
    {
        get; init;
    }
}
