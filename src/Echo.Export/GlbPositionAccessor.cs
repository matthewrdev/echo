namespace Echo.Export;

internal sealed class GlbPositionAccessor
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
    public required float[] Min
    {
        get; init;
    }
    public required float[] Max
    {
        get; init;
    }
}
