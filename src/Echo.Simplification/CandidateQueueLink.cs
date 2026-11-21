namespace Echo.Simplification;

internal readonly record struct CandidateQueueLink(
    int A,
    int B,
    int Next,
    int Slot);
