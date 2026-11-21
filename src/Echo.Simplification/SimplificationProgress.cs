using System.Diagnostics;
using Echo.Models;

namespace Echo.Simplification;

internal sealed class SimplificationProgress
{
    private readonly Action<OperationProgress>? publish;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly string[] steps = ["Prepare vertices", "Prepare triangles", "Measure geometric error", "Index edges",
        "Constrain boundaries", "Rank collapse candidates", "Collapse edges", "Compact mesh"];
    private SimplificationPhase phase;
    private long completed;
    private long? total;
    private string unit = "items";

    public SimplificationProgress(Action<OperationProgress>? publish)
    {
        this.publish = publish;
    }

    public void Start(SimplificationPhase phase, long? total, string unit, long completed = 0)
    {
        this.phase = phase;
        this.total = total;
        this.unit = unit;
        this.completed = completed;
        Report();
    }

    public void Advance(long completed, int? remainingFaces = null, int? queued = null)
    {
        this.completed = completed;
        if (clock.ElapsedMilliseconds >= 125)
        {
            Report(remainingFaces, queued);
        }
    }

    public void Complete(int? remainingFaces = null)
    {
        completed = total ?? completed;
        total = completed;
        Report(remainingFaces);
    }

    private void Report(int? remainingFaces = null, int? queued = null)
    {
        if (publish == null)
        {
            return;
        }
        var title = steps[(int)phase];
        var message = total.HasValue ? $"{title}: {completed:N0} / {total:N0} {unit}." : $"{title}: {completed:N0} {unit} examined.";
        if (remainingFaces.HasValue)
        {
            message += $" {remainingFaces:N0} triangles remain.";
        }
        if (queued.HasValue)
        {
            message += $" {queued:N0} candidates queued.";
        }
        publish(new OperationProgress(title, message, completed, total, unit)
        {
            Steps = steps,
            StepIndex = (int)phase
        });
        clock.Restart();
    }
}
