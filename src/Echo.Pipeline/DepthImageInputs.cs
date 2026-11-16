using Echo.Contracts;

namespace Echo.Pipeline;

internal sealed class DepthImageInputs
{
    public required string Alignment
    {
        get; init;
    }
    public required string ImageId
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
    public required bool DepthSuppressSky
    {
        get; init;
    }
    public required string? SkySemantics
    {
        get; init;
    }
    public required int DepthWidth
    {
        get; init;
    }
    public required int DepthPlanes
    {
        get; init;
    }
    public required int DepthRefinementSamples
    {
        get; init;
    }
    public required int DepthNeighbours
    {
        get; init;
    }
    public required bool DepthRangeAutomatic
    {
        get; init;
    }
    public required double DepthNear
    {
        get; init;
    }
    public required double DepthFar
    {
        get; init;
    }
}
