using Echo.Models;
using Echo.Telemetry;

namespace Echo.Optimization;

internal sealed class BundleProgress
{
    private readonly Action<OperationProgress>? report;
    private readonly IPerformanceTrace? trace;
    private readonly int iterations;
    private readonly string[] operations = ["Build bundle tracks", "Evaluate bundle cost", "Assemble bundle system",
        "Eliminate bundle points", "Solve bundle camera system", "Evaluate bundle update", "Finalize bundle tracks"];
    private readonly string[] steps = ["Build tracks", "Measure error", "Assemble constraints", "Reduce system",
        "Solve cameras", "Check improvement", "Filter tracks"];

    public BundleProgress(Action<OperationProgress>? report, IPerformanceTrace? trace, int iterations)
    {
        this.report = report;
        this.trace = trace;
        this.iterations = iterations;
    }

    public void Report(OperationProgress update, int? iteration = null)
    {
        var progress = update with
        {
            Steps = steps,
            StepIndex = Array.IndexOf(operations, update.Operation),
            Message = iteration.HasValue ? $"Bundle iteration {iteration + 1} of {iterations} · {update.Message}" : update.Message
        };
        trace?.Operation(progress, PerformanceBackend.Cpu);
        report?.Invoke(progress);
    }
}
