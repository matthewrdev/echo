namespace Echo.Contracts;

public sealed class ResourceProgress
{
    public DateTimeOffset SampledAt { get; set; } = DateTimeOffset.UtcNow;
    public long WorkingSetBytes
    {
        get; set;
    }
    public long PeakWorkingSetBytes
    {
        get; set;
    }
    public long ManagedBytes
    {
        get; set;
    }
    public long MemoryBudgetBytes
    {
        get; set;
    }
    public long CacheResidentBytes
    {
        get; set;
    }
    public long CachePeakBytes
    {
        get; set;
    }
    public long DiskReadBytes
    {
        get; set;
    }
    public long DiskWrittenBytes
    {
        get; set;
    }
    public long PageEvictions
    {
        get; set;
    }
    public long ScratchBytes
    {
        get; set;
    }
}
