using Echo.Contracts;

namespace Echo.Pipeline;

internal sealed class GuidedPairInputs
{
    public required int AlgorithmVersion
    {
        get; init;
    }
    public required ComputeBackend ComputeBackend
    {
        get; init;
    }
    public required string ComputeImplementation
    {
        get; init;
    }
    public required string InitialPair
    {
        get; init;
    }
    public required string Left
    {
        get; init;
    }
    public required string Right
    {
        get; init;
    }
    public required double Threshold
    {
        get; init;
    }
    public required double MatchRatioThreshold
    {
        get; init;
    }
    public required double MatchDistanceThreshold
    {
        get; init;
    }
}
