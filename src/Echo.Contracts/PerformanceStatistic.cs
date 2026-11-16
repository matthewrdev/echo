namespace Echo.Contracts;

public sealed record PerformanceStatistic
{
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
    public long Count
    {
        get; set;
    }
    public long ReusedCount
    {
        get; set;
    }
    public long FailedCount
    {
        get; set;
    }
    public double TotalMilliseconds
    {
        get; set;
    }
    public double MaximumMilliseconds
    {
        get; set;
    }
    public double? GpuMilliseconds
    {
        get; set;
    }
    public long Items
    {
        get; set;
    }
    public long Bytes
    {
        get; set;
    }
}
