using Echo.Models;
using Echo.Storage;

namespace Echo.Fusion;

public interface IDepthFusion
{
    FusedVolume Fuse(ProjectData project,
        ReconstructionScene scene,
        IReadOnlyList<DepthMap> maps,
        DenseCloudData cloud,
        FusionResourcePlan resources,
        CancellationToken cancellation, Action<string>? progress = null, WorkBuffers? work = null, IFusionChunkStore? chunks = null, Action<OperationProgress>? operationProgress = null, Action<FusionPreview>? preview = null, IFusionGridStore? grids = null);
}
