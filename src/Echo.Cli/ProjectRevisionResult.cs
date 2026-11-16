namespace Echo.Cli;

internal sealed class ProjectRevisionResult
{
    public required string Id
    {
        get; init;
    }
    public required int Revision
    {
        get; init;
    }
}
