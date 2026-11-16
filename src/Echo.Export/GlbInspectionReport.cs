namespace Echo.Export;

internal sealed class GlbInspectionReport
{
    public required int SchemaVersion
    {
        get; init;
    }
    public required string Path
    {
        get; init;
    }
    public required uint Bytes
    {
        get; init;
    }
    public required int Vertices
    {
        get; init;
    }
    public required int Faces
    {
        get; init;
    }
    public required List<object> Bounds
    {
        get; init;
    }
    public required int Images
    {
        get; init;
    }
    public required List<object> Textures
    {
        get; init;
    }
    public required string Note
    {
        get; init;
    }
}
