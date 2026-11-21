using Echo.Models;

namespace Echo.Storage.Resources;

public sealed record ProcessingResourcePlan(
    long MemoryBytes,
    long CacheBytes,
    long DiskBytes,
    long HeadroomBytes,
    ProcessingMemoryMode Mode,
    int DepthMemoryBudgetMb);
