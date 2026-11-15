using Echo.Contracts;

namespace Echo.Fusion;

public interface IDepthFusion
{
    FusedVolume Fuse(ProjectData project,
        ReconstructionScene scene,
        IReadOnlyList<DepthMap> maps,
        DenseCloudData cloud,
        FusionResourcePlan resources,
        CancellationToken cancellation, Action<string>? progress = null);
}
