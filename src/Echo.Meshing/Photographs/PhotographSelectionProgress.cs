using System.Diagnostics;
using Echo.Models;

namespace Echo.Meshing.Photographs;

internal sealed class PhotographSelectionProgress
{
    private readonly Action<string>? text;
    private readonly Action<OperationProgress>? publish;
    private readonly object gate = new();
    private readonly string[] steps = ["Build visibility index", "Check measured visibility", "Check completed gaps",
        "Build seam neighbours", "Choose coherent photographs", "Assemble mesh"];
    private PhotographSelectionPhase phase;
    private long completed;
    private long? total;
    private long lastReport;

    public PhotographSelectionProgress(Action<string>? text, Action<OperationProgress>? publish = null)
    {
        this.text = text;
        this.publish = publish;
    }

    // Phase changes happen outside worker loops. Only Increment is concurrent.
    public void Start(PhotographSelectionPhase phase, long? total = null)
    {
        this.phase = phase;
        this.total = total;
        completed = 0;
        Report();
    }

    public void Increment()
    {
        Interlocked.Increment(ref completed);
        Update();
    }

    public void Update(string? message = null)
    {
        if (text == null && publish == null)
        {
            return;
        }

        if (Stopwatch.GetTimestamp() - Volatile.Read(ref lastReport) < Stopwatch.Frequency / 8)
        {
            return;
        }

        lock (gate)
        {
            if (Stopwatch.GetTimestamp() - lastReport >= Stopwatch.Frequency / 8)
            {
                Report(message);
            }
        }
    }

    public void Complete()
    {
        if (total.HasValue)
        {
            completed = total.Value;
        }

        Report();
    }

    private void Report(string? message = null)
    {
        Volatile.Write(ref lastReport, Stopwatch.GetTimestamp());
        if (text == null && publish == null)
        {
            return;
        }

        var title = steps[(int)phase];
        var count = Volatile.Read(ref completed);
        message ??= total.HasValue ? $"{title}: {count:N0} / {total:N0} triangles." : title + "…";
        if (publish != null)
        {
            publish(new OperationProgress(title, message, total.HasValue ? count : null, total, "triangles")
            {
                Steps = steps,
                StepIndex = (int)phase
            });
        }
        else
        {
            text?.Invoke(message);
        }
    }
}
