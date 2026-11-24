namespace Echo.Models;

public sealed record RunEvent(long Sequence, DateTimeOffset Timestamp, string RunId, string Type,
    Dictionary<string, object?> Payload)
{
    public int SchemaVersion => 1;
}
