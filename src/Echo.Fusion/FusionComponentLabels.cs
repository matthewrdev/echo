namespace Echo.Fusion;

public sealed class FusionComponentLabels
{
    public required IList<int> Labels
    {
        get; init;
    }
    public required IList<int> Sizes
    {
        get; init;
    }
    public int Largest
    {
        get; init;
    }
    public int RegionCount
    {
        get; init;
    }
}
