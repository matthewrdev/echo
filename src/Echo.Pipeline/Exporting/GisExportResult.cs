namespace Echo.Pipeline.Exporting;

public sealed class GisExportResult
{
    public string Directory { get; set; } = "";
    public string RunId { get; set; } = "";
    public List<string> Files { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public int Cameras
    {
        get; set;
    }
    public int Triangles
    {
        get; set;
    }
}
