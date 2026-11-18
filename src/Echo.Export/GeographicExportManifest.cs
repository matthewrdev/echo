namespace Echo.Export;

public sealed class GeographicExportManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string RunId { get; set; } = "";
    public List<string> Files { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
