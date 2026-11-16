namespace Echo.Cli;

internal sealed class RunCancellationResult
{
    public required string RunId
    {
        get; init;
    }
    public required bool CancellationRequested
    {
        get; init;
    }
}
