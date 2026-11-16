namespace Echo.Export;

internal sealed class GlbPrimitive
{
    public required Dictionary<string, int> Attributes
    {
        get; init;
    }
    public required int Indices
    {
        get; init;
    }
    public required int Material
    {
        get; init;
    }
}
