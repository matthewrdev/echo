using Echo.Contracts;

namespace Echo.Fusion;

public interface IFusionResourcePlanner
{
    FusionResourcePlan Plan(Settings settings, long jobBudgetBytes = 0);
}
