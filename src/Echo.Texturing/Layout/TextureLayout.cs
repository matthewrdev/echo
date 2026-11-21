namespace Echo.Texturing.Layout;

public sealed class TextureLayout
{
    public int MinimumAtlasSize
    {
        get; init;
    }
    public int GutterPixels
    {
        get; init;
    }
    public IReadOnlyList<TexturePatch> Patches { get; init; } = [];
    public double TexelsPerWorldUnit
    {
        get; init;
    }
    public double BaselineTexelsPerWorldUnit
    {
        get; init;
    }
    public int VegetationChartCount
    {
        get; init;
    }
}
