namespace Echo.Models;

public sealed class StoredArtifact
{
    public string Path { get; set; } = "";
    public string Stage { get; set; } = "";
    public long Bytes
    {
        get; set;
    }
    public int FileCount
    {
        get; set;
    }
    public List<string> Runs { get; set; } = [];
    public List<string> Dependencies { get; set; } = [];
}
