namespace Echo.Pipeline;

internal sealed class RunStartedEvent
{
    public required string Target
    {
        get; init;
    }
    public required int Revision
    {
        get; init;
    }
}
