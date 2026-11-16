namespace Echo.Pipeline;

internal sealed class ExportInputs
{
    public required string Texture
    {
        get; init;
    }
    public required string MeshPath
    {
        get; init;
    }
    public required string Alignment
    {
        get; init;
    }
    public required bool ExportValidate
    {
        get; init;
    }
}
