namespace Echo.Models;

public sealed record ArtifactDeletionFingerprint(
    string Token,
    ArtifactDeletionKind Kind,
    string? Artifact,
    List<string> Paths,
    Dictionary<string, string> AffectedRuns,
    List<string> RetainedRuns,
    List<string> RemovedRuns);
