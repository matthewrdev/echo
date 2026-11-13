namespace Echo.Contracts;

public sealed class RunProgress
{
    public string Stage { get; set; } = "";
    public string Message { get; set; } = "";
    public int? Completed
    {
        get; set;
    }
    public int? Total
    {
        get; set;
    }
    public DateTimeOffset Updated { get; set; } = DateTimeOffset.UtcNow;
}
