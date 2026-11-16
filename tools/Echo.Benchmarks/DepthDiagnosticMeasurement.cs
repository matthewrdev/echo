using Echo.Depth;

namespace Echo.Benchmarks;

internal sealed class DepthDiagnosticMeasurement
{
    public required string Name
    {
        get; init;
    }
    public required int Width
    {
        get; init;
    }
    public required int Spacing
    {
        get; init;
    }
    public required double Sigma
    {
        get; init;
    }
    public required int PlaneCount
    {
        get; init;
    }
    public required int Total
    {
        get; init;
    }
    public required int LowTexture
    {
        get; init;
    }
    public required int NoMatch
    {
        get; init;
    }
    public required int Ambiguous
    {
        get; init;
    }
    public required int Accepted
    {
        get; init;
    }
    public required int Supported
    {
        get; init;
    }
    public required DepthSearchRange Range
    {
        get; init;
    }
}
