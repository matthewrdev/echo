namespace Echo.Texturing;

public sealed class TextureLayout
{
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
