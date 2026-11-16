namespace Echo.Cli;

internal sealed class PipelinePlanResult
{
    public required int SchemaVersion
    {
        get; init;
    }
    public required int Revision
    {
        get; init;
    }
    public required IEnumerable<string> Stages
    {
        get; init;
    }
    public required string Reuse
    {
        get; init;
    }
    public required int ActiveImages
    {
        get; init;
    }
}
