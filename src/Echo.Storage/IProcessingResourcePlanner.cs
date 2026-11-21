using Echo.Models;

namespace Echo.Storage;

public interface IProcessingResourcePlanner
{
    ProcessingMemoryTracker CreateTracker();
    ProcessingResourcePlan Plan(Settings settings);
    ProcessingResourcePlan PlanStage(ProcessingResourcePlan resources, string stage);
}
