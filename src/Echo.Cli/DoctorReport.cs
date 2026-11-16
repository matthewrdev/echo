namespace Echo.Cli;

internal sealed class DoctorReport
{
    public required int SchemaVersion
    {
        get; init;
    }
    public required string Version
    {
        get; init;
    }
    public required string Runtime
    {
        get; init;
    }
    public required int Processors
    {
        get; init;
    }
    public required string MetadataEngine
    {
        get; init;
    }
    public required bool MetadataReady
    {
        get; init;
    }
    public required string Backend
    {
        get; init;
    }
    public required string[] Limitations
    {
        get; init;
    }
}
