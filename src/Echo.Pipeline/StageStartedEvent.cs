namespace Echo.Pipeline;

internal sealed class StageStartedEvent : IStageProgressEvent
{
    public required string Stage
    {
        get; init;
    }
}
