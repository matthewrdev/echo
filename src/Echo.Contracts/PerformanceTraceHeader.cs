namespace Echo.Contracts;

public sealed class PerformanceTraceHeader
{
    public int SchemaVersion { get; set; } = 2;
    public PerformanceTraceSummary Summary { get; set; } = new();
    public long SpanBytes
    {
        get; set;
    }
    public List<PerformanceSpan> ActiveSpans { get; set; } = [];
    public List<PerformanceStatistic> Statistics { get; set; } = [];
}
