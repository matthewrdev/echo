using Echo.Compute;
using Echo.Models;

namespace Echo.Benchmarks;

internal sealed class ProfileComputeService : IComputeService
{
    private readonly IComputeService inner;

    public ProfileComputeService(IComputeService inner)
    {
        this.inner = inner;
    }

    public long UploadedBytes
    {
        get; internal set;
    }
    public long ReadbackBytes
    {
        get; internal set;
    }
    public long Dispatches
    {
        get; internal set;
    }
    public long MemoryBudgetBytes => inner.MemoryBudgetBytes;
    public string GetImplementation(ComputeOperation operation) => inner.GetImplementation(operation);
    public bool CanAllocate(long bytes, ComputeBackend backend) => inner.CanAllocate(bytes, backend);
    public void RecordExecution(ComputeExecution execution) => inner.RecordExecution(execution);
    public ComputeScope BeginScope(long bytes) => inner.BeginScope(bytes);

    public IComputeSession? Open(ComputeBackend backend, CancellationToken cancellation)
    {
        var session = inner.Open(backend, cancellation);
        return session == null ? null : new ProfileComputeSession(session, this);
    }
}
