using System.Diagnostics;
using Echo.Contracts;

namespace Echo.Simplification;

internal sealed class ParallelCollapseProgress
{
    private readonly object gate = new();
    private readonly Action<OperationProgress>? publish;
    private readonly long[] examined;
    private readonly int workers;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private int finished;

    public ParallelCollapseProgress(int regions, int workers, Action<OperationProgress>? publish)
    {
        examined = new long[regions];
        this.workers = workers;
        this.publish = publish;
    }

    public void Report(int region, OperationProgress update)
    {
        lock (gate)
        {
            if (update.Operation == "Collapse edges")
            {
                examined[region] = update.Completed ?? 0;
            }
            if (clock.ElapsedMilliseconds >= 250)
            {
                Publish();
            }
        }
    }

    public void Complete()
    {
        lock (gate)
        {
            finished++;
            Publish();
        }
    }

    private void Publish()
    {
        var candidates = examined.Sum();
        publish?.Invoke(new OperationProgress("Parallel edge collapse",
            $"Simplifying {examined.Length} independent regions with {workers} CPU workers: {finished} regions complete; {candidates:N0} candidates examined.",
            candidates, null, "candidates"));
        clock.Restart();
    }
}
