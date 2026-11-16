namespace Echo.Compute;

public sealed class ComputeScope : IDisposable
{
    private readonly Action restore;
    private readonly ComputeScope? parent;
    private readonly object gate = new();
    private readonly HashSet<string> devices = new(StringComparer.Ordinal);
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

    public string[] Devices
    {
        get
        {
            lock (gate)
            {
                return devices.Order(StringComparer.Ordinal).ToArray();
            }
        }
    }

    internal void Record(string device)
    {
        lock (gate)
        {
            devices.Add(device);
        }
        parent?.Record(device);
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
