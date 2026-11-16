using Echo.Contracts;

namespace Echo.Pipeline;

internal sealed class ReconstructionReport
{
    public required int SchemaVersion
    {
        get; init;
    }
    public required int ImageCount
    {
        get; init;
    }
    public required int Cameras
    {
        get; init;
    }
    public required int Points
    {
        get; init;
    }
    public required BundleReport? Bundle
    {
        get; init;
    }
    public required int Faces
    {
        get; init;
    }
    public required int InferredColourFaces
    {
        get; init;
    }
    public required int TextureSize
    {
        get; init;
    }
    public required double CoverageFraction
    {
        get; init;
    }
    public required List<string> Unplaced
    {
        get; init;
    }
    public required List<string> Warnings
    {
        get; init;
    }
    public required string Geometry
    {
        get; init;
    }
    public required string Alignment
    {
        get; init;
    }
    public required string Cloud
    {
        get; init;
    }
    public required string Fusion
    {
        get; init;
    }
    public required int RequestedTriangles
    {
        get; init;
    }
    public required bool TriangleBudgetIsCeiling
    {
        get; init;
    }
    public required bool TargetReached
    {
        get; init;
    }
}
