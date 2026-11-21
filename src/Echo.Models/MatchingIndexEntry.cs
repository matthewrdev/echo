namespace Echo.Models;

public sealed record MatchingIndexEntry(
    int Sequence,
    string Artifact,
    string A,
    string B,
    int PointCount,
    int LineCount);
