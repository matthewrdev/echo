using Echo.Contracts;

namespace Echo.Pipeline;

internal sealed class FeatureImageInputs
{
    public required string Id
    {
        get; init;
    }
    public required string SourceHash
    {
        get; init;
    }
    public required int DetectorVersion
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
    public required bool ScaleInvariantFeatures
    {
        get; init;
    }
    public required bool DetectLines
    {
        get; init;
    }
    public required int LineLimit
    {
        get; init;
    }
    public required int LineMinimumLength
    {
        get; init;
    }
    public required double LineGradientThreshold
    {
        get; init;
    }
    public required bool LineSplitRegions
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
}
