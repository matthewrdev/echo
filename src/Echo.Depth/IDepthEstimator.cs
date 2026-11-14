using Echo.Contracts;
using Echo.Projects;

namespace Echo.Depth;

public interface IDepthEstimator
{
    DepthMap Estimate(IProjectStore store,
        ProjectData project,
        ReconstructionScene scene,
        CameraPose camera,
        CancellationToken cancellation,
        int maxWorkers = 0,
        Action<string>? progress = null,
        Action<DepthLevelMetrics>? metrics = null,
        Action<ProcessingExecution>? execution = null);
}
