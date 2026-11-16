namespace Echo.Export;

internal sealed class GlbEmbeddedTexture
{
    public required string? MimeType
    {
        get; init;
    }
    public required int Bytes
    {
        get; init;
    }
    public required int? Width
    {
        get; init;
    }
    public required int? Height
    {
        get; init;
    }
}
