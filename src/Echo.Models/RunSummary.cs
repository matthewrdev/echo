namespace Echo.Models;

public sealed class RunSummary
{
    public string Id { get; set; } = "";
    public string Status { get; set; } = "";
    public string Target { get; set; } = "";
    public DateTimeOffset Created
    {
        get; set;
    }

    public override string ToString() => Id == "latest" ? "Latest run (live)" : $"{Created.ToLocalTime():dd MMM HH:mm} · {Target} · {Status} · {Id[..8]}";
}
