namespace Echo.Benchmarks;

internal sealed class CoreMeasurement
{
    public required string Stage
    {
        get; init;
    }
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
}
