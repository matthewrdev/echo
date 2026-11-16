namespace Echo.Benchmarks;

internal sealed class PairSelectionMeasurement
{
    public required string Name
    {
        get; init;
    }
    public required int SavedPairCount
    {
        get; init;
    }
    public required int SelectedPairCount
    {
        get; init;
    }
    public required int NewPairCount
    {
        get; init;
    }
    public required double SelectionSeconds
    {
        get; init;
    }
    public required List<object> Checks
    {
        get; init;
    }
}
