namespace Echo.Models;

public sealed class TextureRunOptions
{
    public double? ColourStrength
    {
        get; init;
    }
    public int? SeamWidth
    {
        get; init;
    }

    public void Apply(Settings settings)
    {
        if (ColourStrength.HasValue)
        {
            settings.TextureColourEnhancement = ColourStrength.Value;
        }
        if (SeamWidth.HasValue)
        {
            settings.TextureSeamBlendPixels = SeamWidth.Value;
        }
    }
}
