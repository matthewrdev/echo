namespace Echo.Benchmarks;

internal sealed class MetalDepthComparison
{
    public required int Samples
    {
        get; init;
    }
    public required int Costs
    {
        get; init;
    }
    public required int FiniteCosts
    {
        get; init;
    }
    public required int FiniteSupportChanges
    {
        get; init;
    }
    public required int RoundedCostChanges
    {
        get; init;
    }
    public required double MaxCostError
    {
        get; init;
    }
    public required int AcceptedCpu
    {
        get; init;
    }
    public required int AcceptedGpu
    {
        get; init;
    }
    public required int AcceptanceChanges
    {
        get; init;
    }
    public required int BestPlaneChanges
    {
        get; init;
    }
    public required int DepthChanges
    {
        get; init;
    }
    public required int ConfidenceChanges
    {
        get; init;
    }
    public required double MaxRelativeDepthError
    {
        get; init;
    }
}
