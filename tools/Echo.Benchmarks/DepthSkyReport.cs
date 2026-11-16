namespace Echo.Benchmarks;

internal sealed class DepthSkyReport
{
    public required string Name
    {
        get; init;
    }
    public required int AcceptedBefore
    {
        get; init;
    }
    public required int CandidateAccepted
    {
        get; init;
    }
    public required int Removed
    {
        get; init;
    }
    public required int AcceptedAfter
    {
        get; init;
    }
}
