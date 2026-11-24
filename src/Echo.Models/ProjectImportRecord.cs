namespace Echo.Models;

public sealed class ProjectImportRecord
{
    public int Revision
    {
        get; init;
    }
    public Photo? Photo
    {
        get; init;
    }
    public string? PhotoId
    {
        get; init;
    }
    public string? Set
    {
        get; init;
    }
}
