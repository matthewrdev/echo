namespace Echo.Models;

public sealed class LineDetectionOptions
{
    public double GradientThreshold { get; init; } = .025;
    public bool SplitRegions
    {
        get; init;
    }
}
