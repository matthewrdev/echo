namespace Echo.Contracts;

public sealed class ThreadProgress
{
    public int ProcessId
    {
        get; set;
    }
    public ulong ThreadId
    {
        get; set;
    }
    public string Name { get; set; } = "";
    public double CpuPercent
    {
        get; set;
    }
}
