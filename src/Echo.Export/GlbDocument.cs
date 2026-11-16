namespace Echo.Export;

internal sealed class GlbDocument
{
    public required GlbAsset Asset
    {
        get; init;
    }
    public required int Scene
    {
        get; init;
    }
    public required GlbScene[] Scenes
    {
        get; init;
    }
    public required GlbNode[] Nodes
    {
        get; init;
    }
    public required GlbMesh[] Meshes
    {
        get; init;
    }
    public required GlbMaterial[] Materials
    {
        get; init;
    }
    public required GlbTexture[] Textures
    {
        get; init;
    }
    public required GlbSampler[] Samplers
    {
        get; init;
    }
    public required GlbImage[] Images
    {
        get; init;
    }
    public required GlbBuffer[] Buffers
    {
        get; init;
    }
    public required object[] BufferViews
    {
        get; init;
    }
    public required object[] Accessors
    {
        get; init;
    }
}
