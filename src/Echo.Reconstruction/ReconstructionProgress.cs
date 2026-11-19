using Echo.Contracts;
using Echo.Telemetry;

namespace Echo.Reconstruction;

internal sealed class ReconstructionProgress
{
    private readonly Action<OperationProgress>? report;
    private readonly IPerformanceTrace? trace;
    private readonly string[] steps = ["Verify image geometry", "Select visual seed", "Initialize cameras",
        "Refine camera rotations", "Triangulation", "Register cameras", "Bundle adjustment", "Reference visual reconstruction"];

    public ReconstructionProgress(Action<OperationProgress>? report, IPerformanceTrace? trace)
    {
        this.report = report;
        this.trace = trace;
    }

    public void Report(OperationProgress update)
    {
        var operation = update.Operation == "Register visual cameras" ? "Register cameras" : update.Operation;
        var progress = update with
        {
            Steps = steps,
            StepIndex = Array.IndexOf(steps, operation)
        };
        trace?.Operation(progress);
        report?.Invoke(progress);
    }
}
