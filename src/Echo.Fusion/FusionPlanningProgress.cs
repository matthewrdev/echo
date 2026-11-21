using System.Diagnostics;
using Echo.Models;

namespace Echo.Fusion;

internal sealed class FusionPlanningProgress
{
    private readonly Action<OperationProgress>? publish;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private FusionPhase phase;
    private readonly bool cleanup;
    private string unit = "";
    private long total;
    private long completed;
    private readonly object gate = new();

    public FusionPlanningProgress(Action<OperationProgress>? publish, bool cleanup = false)
    {
        this.publish = publish;
        this.cleanup = cleanup;
    }

    public void Start(FusionPhase phase, long total, string unit)
    {
        lock (gate)
        {
            this.phase = phase;
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
            if (count == completed)
            {
                return;
            }

            completed = count;
            if (completed == total || clock.ElapsedMilliseconds >= 500)
            {
                Report(completed);
            }
        }
    }

    private void Report(long completed)
    {
        var title = FusionProgressPlan.Title(phase);
        publish?.Invoke(FusionProgressPlan.Create(phase, cleanup, $"{title}: {completed:N0} / {total:N0} {unit}", completed, total, unit));
        clock.Restart();
    }
}
