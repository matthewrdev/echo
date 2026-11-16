using Echo.Contracts;

namespace Echo.Compute;

public interface IComputeService
{
    string Implementation
    {
        get;
    }
    long MemoryBudgetBytes
    {
        get;
    }
    bool CanAllocate(long byteCount, ComputeBackend backend);
    ComputeScope BeginScope(long memoryBudgetBytes);
    IComputeSession? Open(ComputeBackend backend, CancellationToken cancellation);
}
