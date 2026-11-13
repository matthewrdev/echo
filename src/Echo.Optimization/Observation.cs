namespace Echo.Optimization;

internal sealed class Observation
{
    public int Camera
    {
        get; init;
    }
    public double[] Pixel { get; init; } = [];
}
