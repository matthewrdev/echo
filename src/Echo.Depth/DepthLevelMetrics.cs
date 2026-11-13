namespace Echo.Depth;

public sealed record DepthLevelMetrics(string ImageId, int Width, int Height, int Neighbours,
    double PreparationSeconds, double SearchSeconds, double FilterSeconds)
{
    public double SearchNear
    {
        get; init;
    }
    public double SearchFar
    {
        get; init;
    }
    public string RangeSource { get; init; } = "";
    public int GlobalSearches
    {
        get; init;
    }
    public int RecoveredSamples
    {
        get; init;
    }
    public int RetainedSamples
    {
        get; init;
    }
}
