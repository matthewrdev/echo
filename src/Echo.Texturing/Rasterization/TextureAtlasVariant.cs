namespace Echo.Texturing.Rasterization;

public sealed class TextureAtlasVariant
{
    public int Size
    {
        get; set;
    }
    public int GutterPixels
    {
        get; set;
    }
    public string File { get; set; } = "";
}
