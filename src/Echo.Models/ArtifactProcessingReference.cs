namespace Echo.Models;

public sealed class ArtifactProcessingReference
{
    public string Stage { get; set; } = "";
    public string? MetadataPath
    {
        get; set;
    }
    public bool Reused
    {
        get; set;
    }
    public string ProvenanceStatus { get; set; } = "recorded";
}
