using System.ComponentModel.Composition;
using Echo.Contracts;

namespace Echo.Storage;

[Export(typeof(IProcessingResourcePlanner)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class ProcessingResourcePlanner : IProcessingResourcePlanner
{
    [ImportingConstructor]
    public ProcessingResourcePlanner()
    {
    }

    public ProcessingMemoryTracker CreateTracker() => new();

    public ProcessingResourcePlan Plan(Settings settings)
    {
        settings.Validate();
        var available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var memory = settings.ProcessingMemoryBudgetMb == 0
            ? Math.Max(256L * 1024 * 1024, available / 2)
            : settings.ProcessingMemoryBudgetMb * 1024L * 1024;
        return new ProcessingResourcePlan(memory, Math.Max(8 * 1024 * 1024, memory / 8),
            settings.ProcessingDiskBudgetMb * 1024L * 1024, Math.Max(64 * 1024 * 1024, memory / 8), settings.ProcessingMemoryMode, settings.DepthMemoryBudgetMb);
    }

    public ProcessingResourcePlan PlanStage(ProcessingResourcePlan resources, string stage)
    {
        if (stage != "mesh" || resources.Mode != ProcessingMemoryMode.Paged)
        {
            return resources;
        }

        // Mesh topology and collapse candidates are the working set, not just
        // an I/O cache. Keep half the run budget available to them, leaving
        // the existing headroom and the remainder for native/managed work.
        return resources with
        {
            CacheBytes = resources.MemoryBytes / 2
        };
    }
}
