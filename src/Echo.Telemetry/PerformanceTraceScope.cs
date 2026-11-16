using Echo.Contracts;

namespace Echo.Telemetry;

public sealed class PerformanceTraceScope : IDisposable
{
    private readonly PerformanceTraceSession session;
    private readonly Action restore;
    private PerformanceTraceScope? operation;
    private bool completed;
    private bool disposed;

    internal PerformanceTraceScope(PerformanceTraceSession session, PerformanceSpan span, Action restore)
    {
        this.session = session;
        Span = span;
        this.restore = restore;
    }

    internal PerformanceSpan Span
    {
        get;
    }

    internal PerformanceTraceScope EffectiveParent
    {
        get
        {
            lock (session.Gate)
            {
                return operation ?? this;
            }
        }
    }

    public void Measure(long? items = null, long bytes = 0, double? gpuMilliseconds = null)
        => session.Measure(Span, items, bytes, gpuMilliseconds);

    internal void Operation(OperationProgress progress, PerformanceBackend? backend)
    {
        lock (session.Gate)
        {
            if (completed)
            {
                return;
            }
            if (operation?.Span.Name != progress.Operation || backend.HasValue && operation?.Span.Backend != backend.Value)
            {
                operation?.Complete(PerformanceStatus.Completed);
                operation = session.Begin(progress.Operation, PerformanceSpanKind.Operation, backend ?? Span.Backend, Span.Stage, this, () => { });
            }
            operation.Span.Items = progress.Completed;
            operation.Span.TotalItems = progress.Total;
            operation.Span.Unit = progress.Unit;
            operation.Span.Detail = progress.Message;
        }
    }

    public void Complete(PerformanceStatus status = PerformanceStatus.Completed)
    {
        lock (session.Gate)
        {
            if (completed)
            {
                return;
            }
            operation?.Complete(status);
            operation = null;
            completed = true;
            session.End(Span, status);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        Complete();
        disposed = true;
        restore();
    }
}
