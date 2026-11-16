namespace Echo.Benchmarks;

internal sealed class FeatureMeasurement
{
    public required int Iteration
    {
        get; init;
    }
    public required double Seconds
    {
        get; init;
    }
    public required long AllocatedBytes
    {
        get; init;
    }
    public required int PhotoCount
    {
        get; init;
    }
    public required int FeatureWidth
    {
        get; init;
    }
    public required int FeatureLimit
    {
        get; init;
    }
    public required bool DetectLines
    {
        get; init;
    }
    public required int Points
    {
        get; init;
    }
    public required int Lines
    {
        get; init;
    }
    public required string Hash
    {
        get; init;
    }
}
