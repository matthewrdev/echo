namespace Echo.Contracts;

public sealed class PerformanceTraceDocument
{
    public int SchemaVersion { get; set; } = 1;
    public PerformanceTraceSummary Summary { get; set; } = new();
    public List<PerformanceSpan> Spans { get; set; } = [];
    public List<PerformanceStatistic> Statistics { get; set; } = [];
}
