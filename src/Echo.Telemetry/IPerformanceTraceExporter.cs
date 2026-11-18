namespace Echo.Telemetry;

public interface IPerformanceTraceExporter
{
    PerformanceTraceExport Export(string runDirectory, string attemptId, string destination, string format);
}
