namespace Echo.Contracts;

public sealed record OperationProgress(string Operation, string Message, long? Completed = null, long? Total = null, string Unit = "items")
{
    public string[]? StageSteps
    {
        get; init;
    }
    public int? StageStepIndex
    {
        get; init;
    }
    public string[]? Steps
    {
        get; init;
    }
    public int? StepIndex
    {
        get; init;
    }
}
