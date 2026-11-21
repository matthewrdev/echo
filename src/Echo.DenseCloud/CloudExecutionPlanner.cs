using System.Runtime.CompilerServices;
using Echo.Contracts;

namespace Echo.DenseCloud;

public sealed class CloudExecutionPlanner : ICloudExecutionPlanner
{
    public CloudExecutionPlanner()
    {
    }

    public CloudExecutionPlan Plan(Settings settings, int width, int height)
    {
        const long scratchBudget = 64 * 1024 * 1024;
        var requestedWorkers = settings.CloudWorkerThreads > 0 ? settings.CloudWorkerThreads : Math.Max(1, Environment.ProcessorCount - 2);
        // Allow for ArrayPool's power-of-two bucket rounding. This bounds worker
        // scratch; source depth maps and the final cloud have separate lifetimes.
        var rowBytes = Math.Max(1, width) * (long)Unsafe.SizeOf<DenseSample>() * 2;
        var memoryWorkers = (int)Math.Max(1, scratchBudget / rowBytes);
        var workers = Math.Min(requestedWorkers, Math.Min(Math.Max(1, height), memoryWorkers));
        var rows = (int)Math.Min(settings.CloudBatchRows, Math.Max(1, scratchBudget / (workers * rowBytes)));
        workers = Math.Min(workers, Math.Max(1, (height + rows - 1) / rows));
        return new CloudExecutionPlan(workers, rows, workers * rows * rowBytes);
    }
}
