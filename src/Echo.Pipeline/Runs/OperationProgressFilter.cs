using Echo.Models;

namespace Echo.Pipeline.Runs;

internal sealed class OperationProgressFilter
{
    private string? stage;
    private OperationProgress? previous;
    private TimeSpan reportedAt;

    public bool Accept(string stage, OperationProgress progress, TimeSpan elapsed)
    {
        var transition = this.stage != stage || previous?.Operation != progress.Operation
            || previous?.StepIndex != progress.StepIndex || previous?.Unit != progress.Unit
            || previous?.StageStepIndex != progress.StageStepIndex;
        var reset = progress.Completed < previous?.Completed || progress.Total != previous?.Total;
        var complete = progress.Total > 0 && progress.Completed >= progress.Total;
        var wasComplete = previous?.Total > 0 && previous.Completed >= previous.Total;
        var changed = progress.Completed != previous?.Completed || progress.Message != previous?.Message;
        var interval = elapsed - reportedAt;
        var percentageAdvanced = progress.Total > 0 && progress.Completed.HasValue
            && Math.Floor(100d * progress.Completed.Value / progress.Total.Value)
                > Math.Floor(100d * (previous?.Completed ?? 0) / progress.Total.Value);
        var countedUpdate = percentageAdvanced && interval >= TimeSpan.FromMilliseconds(125);
        var heartbeatInterval = progress.Total > 0 ? TimeSpan.FromSeconds(5) : TimeSpan.FromMilliseconds(125);
        var heartbeat = changed && interval >= heartbeatInterval;
        var publish = transition || reset || (complete && !wasComplete) || countedUpdate || heartbeat;
        if (!publish)
        {
            return false;
        }

        this.stage = stage;
        previous = progress;
        reportedAt = elapsed;
        return true;
    }
}
