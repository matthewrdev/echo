namespace Echo.Export;

internal sealed class GlbMetallicRoughness
{
    public required GlbTextureReference BaseColorTexture
    {
        get; init;
    }
    public required int MetallicFactor
    {
        get; init;
    }
    public required int RoughnessFactor
    {
        get; init;
    }
}
