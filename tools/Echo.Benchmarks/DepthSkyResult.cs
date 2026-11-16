namespace Echo.Benchmarks;

internal sealed class DepthSkyResult
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
    public required string Directory
    {
        get; init;
    }
}
