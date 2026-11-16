namespace Echo.Pipeline;

internal sealed class MatchingInputs
{
    public required int SelectionVersion
    {
        get; init;
    }
    public required List<string> PairUnits
    {
        get; init;
    }
}
