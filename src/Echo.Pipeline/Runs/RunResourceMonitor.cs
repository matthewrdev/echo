namespace Echo.Pipeline.Runs;

internal sealed class RunResourceMonitor : IDisposable
{
    private readonly RunContext context;
    private readonly CancellationTokenSource cancellation;
    private readonly Timer timer;
    private Exception? failure;

    public RunResourceMonitor(RunContext context, CancellationTokenSource cancellation)
    {
        this.context = context;
        this.cancellation = cancellation;
        timer = new Timer(Poll, null, 1000, 1000);
    }

    public Exception? Failure => Volatile.Read(ref failure);

    private void Poll(object? state)
    {
        if (cancellation.IsCancellationRequested)
        {
            return;
        }
        try
        {
            context.ReportResources();
        }
        catch (Exception error)
        {
            Interlocked.CompareExchange(ref failure, error, null);
            cancellation.Cancel();
        }
    }

    public void Dispose() => timer.DisposeAsync().AsTask().GetAwaiter().GetResult();
}
