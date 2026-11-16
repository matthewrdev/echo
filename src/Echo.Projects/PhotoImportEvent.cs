namespace Echo.Projects;

internal sealed class PhotoImportEvent
{
    public required string Type
    {
        get; init;
    }
    public required string Id
    {
        get; init;
    }
    public required string Name
    {
        get; init;
    }
    public required int Count
    {
        get; init;
    }
}
