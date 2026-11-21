namespace Echo.Models;

public sealed class Artifact
{
    public int SchemaVersion { get; set; } = 1;
    public string AlgorithmVersion { get; set; } = "echo-0.1.0";
    public string Stage { get; set; } = "";
    public string Key { get; set; } = "";
    public Dictionary<string, string> Files { get; set; } = [];
}
