using Echo.Contracts;

namespace Echo.Fusion;

public sealed class FusionResourcePlanner : IFusionResourcePlanner
{
    public FusionResourcePlanner()
    {
    }

    public FusionResourcePlan Plan(Settings settings, long jobBudgetBytes = 0)
    {
        var resources = FusionMemoryBudget.Resolve(settings.FusionMemoryBudgetMb, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);
        if (jobBudgetBytes <= 0 || resources.MemoryBudgetBytes <= jobBudgetBytes)
        {
            return resources;
        }

        // The run has already reserved its cache and native/managed overhead.
        // Fusion may use the remainder without independently claiming that RAM again.
        return new FusionResourcePlan(jobBudgetBytes, (int)Math.Min(int.MaxValue, jobBudgetBytes / 256));
    }
}
