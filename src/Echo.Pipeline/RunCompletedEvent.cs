namespace Echo.Pipeline;

internal sealed class RunCompletedEvent
{
    public required string Stage
    {
        get; init;
    }
    public required Dictionary<string, string> Artifacts
    {
        get; init;
    }
}
