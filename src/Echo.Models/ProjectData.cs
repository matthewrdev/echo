namespace Echo.Models;

public sealed class ProjectData
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Untitled";
    public int Revision
    {
        get; set;
    }
    public List<Photo> Images { get; set; } = [];
    public List<Hint> Hints { get; set; } = [];
    public Dictionary<string, string> StageRevisions { get; set; } = [];
    public Settings Settings { get; set; } = new();
}
