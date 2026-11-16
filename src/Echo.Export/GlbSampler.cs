namespace Echo.Export;

internal sealed class GlbSampler
{
    public required int MagFilter
    {
        get; init;
    }
    public required int MinFilter
    {
        get; init;
    }
    public required int WrapS
    {
        get; init;
    }
    public required int WrapT
    {
        get; init;
    }
}
