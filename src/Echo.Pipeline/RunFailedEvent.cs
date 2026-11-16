namespace Echo.Pipeline;

internal sealed class RunFailedEvent
{
    public required string Error
    {
        get; init;
    }
    public required string Details
    {
        get; init;
    }
}
