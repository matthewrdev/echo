namespace Echo.Simplification;

internal readonly record struct IndexedCandidate(
    Candidate Value,
    double Priority,
    int Handle);
