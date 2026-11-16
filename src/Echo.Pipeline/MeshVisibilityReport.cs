namespace Echo.Pipeline;

internal sealed class MeshVisibilityReport
{
    public required int RejectedBeforeCompletion
    {
        get; init;
    }
    public required int RejectedAfterCompletion
    {
        get; init;
    }
}
