using Echo.Contracts;

namespace Echo.Telemetry;

public interface IPerformanceTrace
{
    PerformanceTraceSession BeginRun(string runId, string target);
    PerformanceTraceScope? Begin(string name, PerformanceSpanKind kind, PerformanceBackend backend = PerformanceBackend.Cpu, string? stage = null);
    void Operation(OperationProgress progress, PerformanceBackend? backend = null);
    void Measurement(string name, double value, string unit = "items");
}
