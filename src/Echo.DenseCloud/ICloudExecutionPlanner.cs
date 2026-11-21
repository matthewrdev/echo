using Echo.Models;

namespace Echo.DenseCloud;

public interface ICloudExecutionPlanner
{
    CloudExecutionPlan Plan(Settings settings, int width, int height);
}
