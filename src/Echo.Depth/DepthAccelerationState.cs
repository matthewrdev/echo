namespace Echo.Depth;

internal sealed class DepthAccelerationState : IDisposable
{
    private string? failure;

    public DepthAccelerationState(IDepthCostSession? session, string? failure)
    {
        Session = session;
        this.failure = failure;
    }

    public IDepthCostSession? Session
    {
        get;
    }
    public string? Failure => Volatile.Read(ref failure);

    public void Fail(string reason)
    {
        Interlocked.CompareExchange(ref failure, reason, null);
    }

    public void Dispose()
    {
        Session?.Dispose();
    }
}
