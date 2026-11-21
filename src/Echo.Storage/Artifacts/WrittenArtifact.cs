namespace Echo.Storage.Artifacts;

internal sealed record WrittenArtifact(
    string Checksum,
    long Length,
    DateTime LastWriteUtc,
    DateTime CreatedUtc);
