namespace Echo.Compute;

public sealed class ComputeScope : IDisposable
{
    private readonly Action restore;
    private readonly ComputeScope? parent;
    private readonly object gate = new();
    private readonly HashSet<ComputeExecution> executions = [];
    private bool disposed;

    internal ComputeScope(long memoryBudgetBytes, ComputeScope? parent, Action restore)
    {
        MemoryBudgetBytes = memoryBudgetBytes;
        this.parent = parent;
        this.restore = restore;
    }

    public long MemoryBudgetBytes
    {
        get;
    }

    public string[] Devices => Executions.Select(execution => execution.Device).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    public ComputeExecution[] Executions
    {
        get
        {
            lock (gate)
            {
                return executions.OrderBy(execution => execution.Operation).ThenBy(execution => execution.Device, StringComparer.Ordinal).ToArray();
            }
        }
    }

    internal void Record(ComputeExecution execution)
    {
        lock (gate)
        {
            executions.Add(execution);
        }
        parent?.Record(execution);
    }

    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            restore();
        }
    }
}
