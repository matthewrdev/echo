using Echo.Contracts;

namespace Echo.Benchmarks;

internal sealed class PairVerificationDecision
{
    public required int Number
    {
        get; init;
    }
    public required Match Match
    {
        get; init;
    }
    public required bool Accepted
    {
        get; init;
    }
}
