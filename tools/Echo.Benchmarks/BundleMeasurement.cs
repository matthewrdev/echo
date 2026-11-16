using Echo.Contracts;

namespace Echo.Benchmarks;

internal sealed class BundleMeasurement
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
    public required string Hash
    {
        get; init;
    }
    public required BundleReport? Bundle
    {
        get; init;
    }
}
