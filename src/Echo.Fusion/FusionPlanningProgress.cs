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

    public FusionPlanningProgress(Action<OperationProgress>? publish)
    {
        this.publish = publish;
    }

    public void Start(string operation, long total, string unit)
    {
        this.operation = operation;
        this.total = total;
        this.unit = unit;
        Report(0);
    }

    public void Advance(long completed)
    {
        if (completed == total || clock.ElapsedMilliseconds >= 500)
        {
            Report(completed);
        }
    }

    private void Report(long completed)
    {
        publish?.Invoke(new OperationProgress(operation, $"{operation}: {completed:N0} / {total:N0} {unit}", completed, total, unit));
        clock.Restart();
    }
}
