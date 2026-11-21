using Echo.Models;

namespace Echo.Fusion;

public interface IFusionResourcePlanner
{
    FusionResourcePlan Plan(Settings settings, long jobBudgetBytes = 0);
}
