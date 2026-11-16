using Echo.Contracts;

namespace Echo.Pipeline;

internal sealed class MatchPairInputs
{
    public required string A
    {
        get; init;
    }
    public required string B
    {
        get; init;
    }
    public required Hint[] Hints
    {
        get; init;
    }
    public required int MatcherVersion
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
    public required double MatchRatioThreshold
    {
        get; init;
    }
    public required double MatchDistanceThreshold
    {
        get; init;
    }
}
