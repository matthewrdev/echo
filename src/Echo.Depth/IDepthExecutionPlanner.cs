using Echo.Contracts;

namespace Echo.Depth;

public interface IDepthExecutionPlanner
{
    DepthExecutionPlan Plan(ProjectData project, int cameraCount, long jobBudgetBytes = 0);
}
