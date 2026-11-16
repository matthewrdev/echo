namespace Echo.Cli;

internal sealed class PipelineResetResult
{
    public required int SchemaVersion
    {
        get; init;
    }
    public required IReadOnlyList<string> ResetStages
    {
        get; init;
    }
    public required string Message
    {
        get; init;
    }
}
