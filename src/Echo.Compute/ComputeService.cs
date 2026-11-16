using Echo.Contracts;
using System.ComponentModel.Composition;

namespace Echo.Compute;

[Export(typeof(IComputeService)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class ComputeService : IComputeService
{
    private readonly IComputeDevice[] devices;
    private readonly AsyncLocal<ComputeScope?> scope = new();

    [ImportingConstructor]
    public ComputeService([ImportMany] IEnumerable<IComputeDevice> devices)
    {
        this.devices = devices.ToArray();
    }

    public string Implementation => string.Join(";", devices.Select(device => device.Implementation));

    public long MemoryBudgetBytes => scope.Value?.MemoryBudgetBytes ?? 512L * 1024 * 1024;

    public bool CanAllocate(long byteCount, ComputeBackend backend)
    {
        if (backend == ComputeBackend.Cpu)
        {
            return false;
        }
        if (byteCount >= 0 && byteCount <= MemoryBudgetBytes)
        {
            return true;
        }
        if (backend == ComputeBackend.Metal)
        {
            throw new ComputeException("The requested compute operation exceeds the Metal session memory budget.");
        }
        return false;
    }

    public ComputeScope BeginScope(long memoryBudgetBytes)
    {
        var previous = scope.Value;
        var current = new ComputeScope(Math.Clamp(memoryBudgetBytes, 4, MemoryBudgetBytes), previous, () => scope.Value = previous);
        scope.Value = current;
        return current;
    }

    public IComputeSession? Open(ComputeBackend backend, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(backend))
        {
            throw new ArgumentOutOfRangeException(nameof(backend));
        }
        if (backend == ComputeBackend.Cpu)
        {
            return null;
        }
        foreach (var device in devices)
        {
            try
            {
                var session = device.Open(cancellation, MemoryBudgetBytes);
                if (session != null)
                {
                    return new ObservedComputeSession(session, () => scope.Value?.Record(session.Device));
                }
            }
            catch (ComputeException) when (backend == ComputeBackend.Auto)
            {
                // A missing or incompatible GPU implementation cannot prevent an
                // automatic run from using the managed processing paths.
            }
        }
        if (backend == ComputeBackend.Metal)
        {
            throw new ComputeException("Metal processing was requested but no Metal compute device is available.");
        }
        return null;
    }
}
