namespace Echo.Export;

internal sealed class GlbMaterial
{
    public required bool DoubleSided
    {
        get; init;
    }
    public required GlbMetallicRoughness PbrMetallicRoughness
    {
        get; init;
    }
}
