namespace Echo.Storage;

public readonly record struct MemoryUsage(long ResidentBytes, long PeakBytes, long ManagedBytes);
