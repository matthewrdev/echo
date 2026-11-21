using Echo.Contracts;

namespace Echo.Telemetry;

public sealed class PerformanceTraceService : IPerformanceTrace
{
    private readonly AsyncLocal<PerformanceTraceSession?> session = new();
    private readonly AsyncLocal<PerformanceTraceScope?> scope = new();

    public PerformanceTraceService()
    {
    }

    public PerformanceTraceSession BeginRun(string runId, string target)
    {
        var previous = session.Value;
        var previousScope = scope.Value;
        var current = new PerformanceTraceSession(runId, target, () =>
        {
            session.Value = previous;
            scope.Value = previousScope;
        });
        session.Value = current;
        scope.Value = null;
        return current;
    }

    public PerformanceTraceScope? Begin(string name, PerformanceSpanKind kind, PerformanceBackend backend = PerformanceBackend.Cpu, string? stage = null)
    {
        var current = session.Value;
        if (current == null)
        {
            return null;
        }
        var previous = scope.Value;
        var parent = previous?.EffectiveParent;
        var result = current.Begin(name, kind, backend, stage ?? parent?.Span.Stage ?? "preparation", parent,
            () => scope.Value = previous);
        scope.Value = result;
        return result;
    }

    public void Operation(OperationProgress progress, PerformanceBackend? backend = null) => scope.Value?.Operation(progress, backend);

    public void Measurement(string name, double value, string unit = "items")
        => session.Value?.Measurement(scope.Value?.Span.Stage ?? "preparation", name, value, unit);
}
