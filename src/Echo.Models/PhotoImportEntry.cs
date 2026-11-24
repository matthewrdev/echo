namespace Echo.Models;

public sealed class PhotoImportEntry
{
    public PhotoImportEntry(string path, string set)
    {
        Path = path;
        Set = set;
    }

    public string Path
    {
        get;
    }

    public string Set
    {
        get;
    }
}
