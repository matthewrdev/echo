namespace Echo.Cli;

internal sealed class CommandError
{
    public required int SchemaVersion
    {
        get; init;
    }
    public required string Error
    {
        get; init;
    }
    public required int ExitCode
    {
        get; init;
    }
}
