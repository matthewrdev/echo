namespace Echo.Benchmarks;

internal sealed class PairVerificationReport
{
    public required string Unit
    {
        get; init;
    }
    public required string A
    {
        get; init;
    }
    public required string B
    {
        get; init;
    }
    public required int Candidates
    {
        get; init;
    }
    public required int Inliers
    {
        get; init;
    }
    public required int Lines
    {
        get; init;
    }
    public required List<object> Decisions
    {
        get; init;
    }
}
