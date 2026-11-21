using System.Text.Json.Serialization;

namespace Echo.Models;

public sealed record PerformanceStatistic
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? BusyMilliseconds
    {
        get; set;
    }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaximumConcurrency
    {
        get; set;
    }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? MedianUpperMilliseconds
    {
        get; set;
    }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? P95UpperMilliseconds
    {
        get; set;
    }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ThreadAllocatedBytes
    {
        get; set;
    }
    public long ThreadAllocationSamples
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
