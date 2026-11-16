namespace Echo.Export;

internal sealed class GlbTargetBufferView
{
    public required int Buffer
    {
        get; init;
    }
    public required int ByteOffset
    {
        get; init;
    }
    public required int ByteLength
    {
        get; init;
    }
    public required int Target
    {
        get; init;
    }
}
