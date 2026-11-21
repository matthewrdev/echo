namespace Echo.Models;

public sealed record PerformanceTraceSummary
{
    public long DroppedMeasurements
    {
        get; set;
    }
    public PerformanceTraceEnvironment? Environment
    {
        get; set;
    }
    public string Id { get; set; } = "";
    public string RunId { get; set; } = "";
    public string Target { get; set; } = "";
    public DateTimeOffset StartedAt
    {
        get; set;
    }
    public DateTimeOffset UpdatedAt
    {
        get; set;
    }
    public PerformanceStatus Status
    {
        get; set;
    }
    public double DurationMilliseconds
    {
        get; set;
    }
    public double CpuMilliseconds
    {
        get; set;
    }
    public long DroppedSpans
    {
        get; set;
    }
}
