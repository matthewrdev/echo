using System.Text.Json.Serialization;

namespace Echo.Contracts;

public sealed record PerformanceSpan
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ThreadAllocatedBytes
    {
        get; set;
    }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ProcessAllocatedBytes
    {
        get; set;
    }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ProcessCpuMilliseconds
    {
        get; set;
    }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? GcPauseMilliseconds
    {
        get; set;
    }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Gen0Collections
    {
        get; set;
    }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Gen1Collections
    {
        get; set;
    }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Gen2Collections
    {
        get; set;
    }
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
