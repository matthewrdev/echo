using Echo.Contracts;

namespace Echo.DenseCloud;

public interface ICloudExecutionPlanner
{
    CloudExecutionPlan Plan(Settings settings, int width, int height);
}
