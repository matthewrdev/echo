namespace Echo.Pipeline;

internal sealed class FeatureImageCheckpointEvent : IStageProgressEvent
{
    long? IStageProgressEvent.Completed => Completed;
    long? IStageProgressEvent.Total => Total;
    public required string Stage
    {
        get; init;
    }
    public required string ImageId
    {
        get; init;
    }
    public required string ImageName
    {
        get; init;
    }
    public required int PointCount
    {
        get; init;
    }
    public required int LineCount
    {
        get; init;
    }
    public required bool Reused
    {
        get; init;
    }
    public required string Message
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
