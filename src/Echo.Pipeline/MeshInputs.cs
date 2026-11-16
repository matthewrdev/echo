using Echo.Contracts;

namespace Echo.Pipeline;

internal sealed class MeshInputs
{
    public required string FusionPath
    {
        get; init;
    }
    public required List<string> DepthUnits
    {
        get; init;
    }
    public required string Alignment
    {
        get; init;
    }
    public required int CompletionVersion
    {
        get; init;
    }
    public required int SimplificationVersion
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
    public required int PhotographSelectionVersion
    {
        get; init;
    }
    public required int VegetationVersion
    {
        get; init;
    }
    public required string? SemanticsPath
    {
        get; init;
    }
    public required bool OptimiseVegetation
    {
        get; init;
    }
    public required double VegetationMeshToleranceMultiplier
    {
        get; init;
    }
    public required int FaceLimit
    {
        get; init;
    }
    public required IslandTrimMode MeshIslandMode
    {
        get; init;
    }
    public required double MeshIslandMinimumAreaPercent
    {
        get; init;
    }
}
