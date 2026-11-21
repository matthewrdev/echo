namespace Echo.Models;

public sealed class ProcessorProgress
{
    public double? CpuPercent
    {
        get; set;
    }
    public int LogicalProcessors
    {
        get; set;
    }
    public List<ThreadProgress>? Threads
    {
        get; set;
    }
}
