namespace Echo.Export;

internal sealed class GlbAccessor
{
    public required int BufferView
    {
        get; init;
    }
    public required int ComponentType
    {
        get; init;
    }
    public required int Count
    {
        get; init;
    }
    public required string Type
    {
        get; init;
    }
}
