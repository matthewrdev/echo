using Echo.Meshing;

namespace Echo.Pipeline;

internal sealed class MeshReport
{
    public required int VegetationRemovedFaces
    {
        get; init;
    }
    public required int ExtractedFaces
    {
        get; init;
    }
    public required int ReducedFaces
    {
        get; init;
    }
    public required int TexturedFaces
    {
        get; init;
    }
    public required int InferredColourFaces
    {
        get; init;
    }
    public required PhotographCoverageReport PhotographCoverage
    {
        get; init;
    }
    public required int RequestedFaceLimit
    {
        get; init;
    }
    public required double VoxelSize
    {
        get; init;
    }
    public required double DetailTolerance
    {
        get; init;
    }
    public required bool Completed
    {
        get; init;
    }
    public required MeshVisibilityReport VisibilityFiltering
    {
        get; init;
    }
    public required SurfaceHoleRepairResult FinalHoleRepair
    {
        get; init;
    }
    public required double CompletionDistance
    {
        get; init;
    }
    public required MeshIslandReport IslandTrimming
    {
        get; init;
    }
    public required string Method
    {
        get; init;
    }
    public required string Note
    {
        get; init;
    }
}
