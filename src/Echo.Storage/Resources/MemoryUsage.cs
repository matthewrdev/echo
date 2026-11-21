namespace Echo.Storage.Resources;

public readonly record struct MemoryUsage(
    long ResidentBytes,
    long PeakBytes,
    long ManagedBytes);
