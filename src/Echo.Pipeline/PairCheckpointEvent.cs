namespace Echo.Pipeline;

internal sealed class PairCheckpointEvent : IStageProgressEvent
{
    long? IStageProgressEvent.Completed => Completed;
    long? IStageProgressEvent.Total => Total;
    public required string Stage
    {
        get; init;
    }
    public required string A
    {
        get; init;
    }
    public required string B
    {
        get; init;
    }
    public required int Completed
    {
        get; init;
    }
    public required int Total
    {
        get; init;
    }
}
