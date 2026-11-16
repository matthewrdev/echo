namespace Echo.Contracts;

public sealed record PerformanceSpan
{
    public long Id
    {
        get; set;
    }
    public long? ParentId
    {
        get; set;
    }
    public string Stage { get; set; } = "";
    public string Name { get; set; } = "";
    public PerformanceSpanKind Kind
    {
        get; set;
    }
    public PerformanceBackend Backend
    {
        get; set;
    }
    public PerformanceStatus Status { get; set; } = PerformanceStatus.Running;
    public int ThreadId
    {
        get; set;
    }
    public double StartMilliseconds
    {
        get; set;
    }
    public double DurationMilliseconds
    {
        get; set;
    }
    public double? GpuMilliseconds
    {
        get; set;
    }
    public long? Items
    {
        get; set;
    }
    public long? TotalItems
    {
        get; set;
    }
    public string Unit { get; set; } = "items";
    public long Bytes
    {
        get; set;
    }
    public string? Detail
    {
        get; set;
    }
}
