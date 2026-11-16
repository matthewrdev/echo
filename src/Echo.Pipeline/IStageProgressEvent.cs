namespace Echo.Pipeline;

internal interface IStageProgressEvent
{
    string Stage
    {
        get;
    }
    long? Completed => null;
    long? Total => null;
    string? Message => null;
    string? Unit => null;
    string? Operation => null;
    string[]? Steps => null;
    int? StepIndex => null;
}
