using Echo.Contracts;

namespace Echo.Pipeline;

internal sealed class DenseCloudInputs
{
    public required List<string> DepthUnits
    {
        get; init;
    }
    public required string Alignment
    {
        get; init;
    }
    public required int CloudVersion
    {
        get; init;
    }
    public required ComputeBackend ComputeBackend
    {
        get; init;
    }
    public required string ComputeImplementation
    {
        get; init;
    }
    public required int CloudNeighbours
    {
        get; init;
    }
    public required int CloudMinimumSupport
    {
        get; init;
    }
    public required double CloudDepthTolerance
    {
        get; init;
    }
    public required double CloudReprojectionTolerancePixels
    {
        get; init;
    }
    public required double CloudMinimumParallaxDegrees
    {
        get; init;
    }
}
