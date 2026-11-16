using Echo.Compute;
using Echo.Contracts;

namespace Echo.Tests;

internal sealed class FailingFusionComputeService : IComputeService
{
    private readonly IComputeService inner;

    public FailingFusionComputeService(IComputeService inner)
    {
        this.inner = inner;
    }

    public string GetImplementation(ComputeOperation operation) => inner.GetImplementation(operation);
    public long MemoryBudgetBytes => inner.MemoryBudgetBytes;
    public bool CanAllocate(long bytes, ComputeBackend backend) => inner.CanAllocate(bytes, backend);
    public void RecordExecution(ComputeExecution execution) => inner.RecordExecution(execution);
    public ComputeScope BeginScope(long bytes) => inner.BeginScope(bytes);
    public IComputeSession? Open(ComputeBackend backend, CancellationToken cancellation)
    {
        var session = inner.Open(backend, cancellation);
        return session == null ? null : new FailingFusionComputeSession(session);
    }
}
