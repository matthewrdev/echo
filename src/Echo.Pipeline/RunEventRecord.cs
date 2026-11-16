namespace Echo.Pipeline;

internal sealed class RunEventRecord
{
    public required int SchemaVersion
    {
        get; init;
    }
    public required long Sequence
    {
        get; init;
    }
    public required DateTimeOffset Timestamp
    {
        get; init;
    }
    public required string RunId
    {
        get; init;
    }
    public required string Type
    {
        get; init;
    }
    public required object Payload
    {
        get; init;
    }
}
