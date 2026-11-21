using Echo.Models;

namespace Echo.Storage.Resources;

public interface IProcessingResourcePlanner
{
    ProcessingMemoryTracker CreateTracker();
    ProcessingResourcePlan Plan(Settings settings);
    ProcessingResourcePlan PlanStage(ProcessingResourcePlan resources, string stage);
}
