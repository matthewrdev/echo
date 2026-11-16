namespace Echo.Pipeline;

public sealed class GisExportResult
{
    public string Directory { get; set; } = "";
    public string RunId { get; set; } = "";
    public int Cameras
    {
        get; set;
    }
    public int Triangles
    {
        get; set;
    }
}
