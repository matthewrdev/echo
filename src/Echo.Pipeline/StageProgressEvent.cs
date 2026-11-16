namespace Echo.Pipeline;

internal sealed class StageProgressEvent : IStageProgressEvent
{
    long? IStageProgressEvent.Completed => Completed;
    long? IStageProgressEvent.Total => Total;
    public required string Stage
    {
        get; init;
    }
    public required string? Message
    {
        get; init;
    }
    public required int? Completed
    {
        get; init;
    }
    public required int? Total
    {
        get; init;
    }
    public required string Unit
    {
        get; init;
    }
}
