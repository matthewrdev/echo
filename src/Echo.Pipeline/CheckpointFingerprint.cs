namespace Echo.Pipeline;

internal sealed class CheckpointFingerprint
{
    public required int StorageVersion
    {
        get; init;
    }
    public required string Version
    {
        get; init;
    }
    public required string Stage
    {
        get; init;
    }
    public required object Inputs
    {
        get; init;
    }
}
