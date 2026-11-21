using Echo.Models;

namespace Echo.Compute;

public interface IComputeService
{
    string GetImplementation(ComputeOperation operation);
    long MemoryBudgetBytes
    {
        get;
    }
    bool CanAllocate(long byteCount, ComputeBackend backend);
    void RecordExecution(ComputeExecution execution);
    ComputeScope BeginScope(long memoryBudgetBytes);
    IComputeSession? Open(ComputeBackend backend, CancellationToken cancellation);
}
