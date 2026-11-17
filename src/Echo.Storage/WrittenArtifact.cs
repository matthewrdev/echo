namespace Echo.Storage;

internal sealed record WrittenArtifact(string Checksum, long Length, DateTime LastWriteUtc, DateTime CreatedUtc);
