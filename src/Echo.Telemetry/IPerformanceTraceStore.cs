using Echo.Models;

namespace Echo.Telemetry;

public interface IPerformanceTraceStore
{
    void RecoverInterrupted(string runDirectory, bool cancelled);
    void Save(string runDirectory, PerformanceTraceDocument trace);
    IReadOnlyList<PerformanceTraceSummary> List(string runDirectory);
    PerformanceTraceDocument Read(string runDirectory, string attemptId, bool includeSpans = true);
}
