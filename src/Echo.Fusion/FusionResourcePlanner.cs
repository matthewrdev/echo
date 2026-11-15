using System.ComponentModel.Composition;
using Echo.Contracts;

namespace Echo.Fusion;

[Export(typeof(IFusionResourcePlanner)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class FusionResourcePlanner : IFusionResourcePlanner
{
    [ImportingConstructor]
    public FusionResourcePlanner()
    {
    }

    public FusionResourcePlan Plan(Settings settings)
    {
        return FusionMemoryBudget.Resolve(settings.FusionDetailMultiplier, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);
    }
}
