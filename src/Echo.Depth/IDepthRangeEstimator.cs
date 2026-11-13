using Echo.Contracts;

namespace Echo.Depth;

public interface IDepthRangeEstimator
{
    DepthSearchRange Estimate(ProjectData project, ReconstructionScene scene, Photo photo, CameraPose camera, CancellationToken cancellation);
}
