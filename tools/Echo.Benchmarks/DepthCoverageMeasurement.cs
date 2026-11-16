namespace Echo.Benchmarks;

internal sealed class DepthCoverageMeasurement
{
    public required string Name
    {
        get; init;
    }
    public required string Label
    {
        get; init;
    }
    public required int Total
    {
        get; init;
    }
    public required int Accepted
    {
        get; init;
    }
    public required int Supported
    {
        get; init;
    }
    public required double Coverage
    {
        get; init;
    }
}
