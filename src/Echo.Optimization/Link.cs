namespace Echo.Optimization;

internal sealed class Link
{
    public Observation A { get; init; } = new();
    public Observation B { get; init; } = new();
    public double Score
    {
        get; init;
    }
}
