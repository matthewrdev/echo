using Echo.Models;
using Echo.Telemetry;

namespace Echo.Compute;

public sealed class ComputeService : IComputeService
{
    private readonly IComputeDevice[] devices;
    private readonly IPerformanceTrace trace;
    private readonly AsyncLocal<ComputeScope?> scope = new();

    public ComputeService(IEnumerable<IComputeDevice> devices, IPerformanceTrace trace)
    {
        this.devices = devices.ToArray();
        this.trace = trace;
    }

    public string GetImplementation(ComputeOperation operation) => string.Join(";", devices.Select(device => device.GetImplementation(operation)));

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

    public void RecordExecution(ComputeExecution execution) => scope.Value?.Record(execution);

    public ComputeScope BeginScope(long memoryBudgetBytes)
    {
        var previous = scope.Value;
        var current = new ComputeScope(Math.Clamp(memoryBudgetBytes, 4, previous?.MemoryBudgetBytes ?? long.MaxValue), previous, () => scope.Value = previous);
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
                using var opening = trace.Begin("Acquire Metal session", PerformanceSpanKind.Wait, PerformanceBackend.Metal);
                var session = device.Open(cancellation, MemoryBudgetBytes);
                opening?.Complete();
                if (session != null)
                {
                    var executions = new Dictionary<ComputeOperation, ComputeExecution>();
                    return new ObservedComputeSession(session, kernel =>
                    {
                        var operation = ComputeKernelOperation.For(kernel);
                        if (!executions.TryGetValue(operation, out var execution))
                        {
                            execution = new ComputeExecution
                            {
                                Operation = operation,
                                Backend = ComputeBackend.Metal,
                                Precision = ComputePrecision.Float32,
                                Device = session.Device,
                                Implementation = device.GetImplementation(operation)
                            };
                            executions.Add(operation, execution);
                        }
                        RecordExecution(execution);
                    }, trace);
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
