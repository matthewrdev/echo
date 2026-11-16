using Echo.Contracts;

namespace Echo.Storage;

public sealed record ProcessingResourcePlan(long MemoryBytes, long CacheBytes, long DiskBytes, long HeadroomBytes, ProcessingMemoryMode Mode);
