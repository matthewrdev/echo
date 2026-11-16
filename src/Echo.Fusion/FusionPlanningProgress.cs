using System.Diagnostics;
using Echo.Contracts;

namespace Echo.Fusion;

internal sealed class FusionPlanningProgress
{
    private readonly Action<OperationProgress>? publish;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private string operation = "";
    private string unit = "";
    private long total;
    private long completed;
    private readonly object gate = new();

    public FusionPlanningProgress(Action<OperationProgress>? publish)
    {
        this.publish = publish;
    }

    public void Start(string operation, long total, string unit)
    {
        lock (gate)
        {
            this.operation = operation;
            this.total = total;
            this.unit = unit;
            completed = 0;
            Report(0);
        }
    }

    public void AdvanceBy(long count)
    {
        lock (gate)
        {
            Advance(completed + count);
        }
    }

    public void Advance(long count)
    {
        lock (gate)
        {
            completed = count;
            if (completed == total || clock.ElapsedMilliseconds >= 500)
            {
                Report(completed);
            }
        }
    }

    private void Report(long completed)
    {
        publish?.Invoke(new OperationProgress(operation, $"{operation}: {completed:N0} / {total:N0} {unit}", completed, total, unit));
        clock.Restart();
    }
}
