namespace Echo.Contracts;

public sealed record OperationProgress(string Operation, string Message, long? Completed = null, long? Total = null, string Unit = "items")
{
    public string[]? Steps
    {
        get; init;
    }
    public int? StepIndex
    {
        get; init;
    }
}
