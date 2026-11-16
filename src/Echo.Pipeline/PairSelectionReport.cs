namespace Echo.Pipeline;

internal sealed class PairSelectionReport
{
    public required int SelectionVersion
    {
        get; init;
    }
    public required int FrustumPairNeighbours
    {
        get; init;
    }
    public required double PairSearchDistance
    {
        get; init;
    }
    public required IReadOnlyList<PairIdentity> Pairs
    {
        get; init;
    }
}
