using Echo.Contracts;

namespace Echo.Pipeline;

internal sealed class TextureInputs
{
    public required string MeshPath
    {
        get; init;
    }
    public required string Alignment
    {
        get; init;
    }
    public required int TextureSize
    {
        get; init;
    }
    public required int TextureVersion
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
    public required string PackingImplementation
    {
        get; init;
    }
    public required double VegetationTextureScale
    {
        get; init;
    }
    public required int TextureJpegQuality
    {
        get; init;
    }
    public required int TextureChartFaces
    {
        get; init;
    }
    public required double TextureChartAngle
    {
        get; init;
    }
    public required int TextureGutter
    {
        get; init;
    }
    public required bool TextureSuppressSky
    {
        get; init;
    }
    public required int TextureSkyMarginPixels
    {
        get; init;
    }
}
