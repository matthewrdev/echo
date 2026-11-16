namespace Echo.Contracts;

public sealed class RunProgress
{
    public string[] Steps { get; set; } = [];
    public int? StepIndex
    {
        get; set;
    }
    public ResourceProgress? Resources
    {
        get; set;
    }
    public string Operation { get; set; } = "";
    public string Unit { get; set; } = "items";
    public DateTimeOffset OperationStarted { get; set; } = DateTimeOffset.UtcNow;
    public string Phase { get; set; } = "processing";
    public string Stage { get; set; } = "";
    public string Message { get; set; } = "";
    public long? Completed
    {
        get; set;
    }
    public long? Total
    {
        get; set;
    }
    public DateTimeOffset Updated { get; set; } = DateTimeOffset.UtcNow;
}
