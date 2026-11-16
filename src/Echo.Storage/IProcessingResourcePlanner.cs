using Echo.Contracts;

namespace Echo.Storage;

public interface IProcessingResourcePlanner
{
    ProcessingMemoryTracker CreateTracker();
    ProcessingResourcePlan Plan(Settings settings);
}
