namespace Echo.Models;

public sealed class RunProcessingMetadata
{
    public int SchemaVersion { get; set; } = 1;
    public string RunId { get; set; } = "";
    public string RequestedDepthBackend { get; set; } = "cpu";
    public Dictionary<string, ArtifactProcessingReference> Artifacts { get; set; } = [];
}
