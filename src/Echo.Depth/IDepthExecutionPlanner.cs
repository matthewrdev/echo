using Echo.Models;

namespace Echo.Depth;

public interface IDepthExecutionPlanner
{
    DepthExecutionPlan Plan(ProjectData project, int cameraCount, long jobBudgetBytes = 0, int? depthMemoryBudgetMb = null);
}
