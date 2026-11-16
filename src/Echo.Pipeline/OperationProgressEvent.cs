namespace Echo.Pipeline;

internal sealed class OperationProgressEvent : IStageProgressEvent
{
    public required string Stage
    {
        get; init;
    }
    public required string Operation
    {
        get; init;
    }
    public required string[]? Steps
    {
        get; init;
    }
    public required int? StepIndex
    {
        get; init;
    }
    public required string Message
    {
        get; init;
    }
    public required long? Completed
    {
        get; init;
    }
    public required long? Total
    {
        get; init;
    }
    public required string Unit
    {
        get; init;
    }
}
