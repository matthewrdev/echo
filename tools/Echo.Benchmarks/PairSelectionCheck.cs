using Echo.Contracts;

namespace Echo.Benchmarks;

internal sealed class PairSelectionCheck
{
    public required string Name
    {
        get; init;
    }
    public required GeoPosition? Gps
    {
        get; init;
    }
    public required Attitude? Attitude
    {
        get; init;
    }
    public required bool AlreadyTested
    {
        get; init;
    }
    public required int CandidateMatches
    {
        get; init;
    }
    public required int GeometricallyVerified
    {
        get; init;
    }
}
