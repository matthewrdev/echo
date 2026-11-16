namespace Echo.Pipeline;

internal sealed class ResetCheckpointInputs
{
    public required string ResetRevision
    {
        get; init;
    }
    public required object Inputs
    {
        get; init;
    }
}
