namespace Echo.Models;

public sealed record ArtifactProcessingRecord(
    string Artifact,
    ArtifactProcessingReference Reference);
