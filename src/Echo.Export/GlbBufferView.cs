namespace Echo.Export;

internal sealed class GlbBufferView
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
}
