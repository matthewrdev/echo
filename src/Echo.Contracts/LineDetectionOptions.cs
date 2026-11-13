namespace Echo.Contracts;

public sealed class LineDetectionOptions
{
    public double GradientThreshold { get; init; } = .025;
    public bool SplitRegions
    {
        get; init;
    }
}
