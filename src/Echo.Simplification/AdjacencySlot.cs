namespace Echo.Simplification;

internal readonly record struct AdjacencySlot(
    int Face,
    int Next,
    int FreeNext,
    int Previous);
