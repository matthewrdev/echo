using Echo.Contracts;

namespace Echo.Benchmarks;

internal sealed class AlignmentMeasurement
{
    public required double Seconds
    {
        get; init;
    }
    public required BundleReport? Bundle
    {
        get; init;
    }
}
