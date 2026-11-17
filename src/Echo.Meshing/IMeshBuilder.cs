using Echo.Contracts;
using Echo.Storage;

namespace Echo.Meshing;

public interface IMeshBuilder
{
    Surface Extract(FusedVolume volume, CancellationToken cancellation, WorkBuffers? work = null, Action<string>? progress = null, Action<OperationProgress>? operationProgress = null, ComputeBackend backend = ComputeBackend.Auto);
    void FillHoles(ProjectData project, ReconstructionScene scene, IReadOnlyList<DepthMap> maps,
        Surface geometry, double voxelSize, CancellationToken cancellation, WorkBuffers? work = null, Action<OperationProgress>? operationProgress = null, MeshDepthContext? depthContext = null);

    SurfaceHoleRepairResult RepairFinalHoles(ProjectData project, ReconstructionScene scene, IReadOnlyList<DepthMap> maps,
        Surface geometry, double voxelSize, CancellationToken cancellation, WorkBuffers? work = null, Action<OperationProgress>? operationProgress = null, MeshDepthContext? depthContext = null);
    Surface AssignCameras(ProjectData project,
        ReconstructionScene scene,
        IReadOnlyList<DepthMap> maps,
        Surface geometry,
        CancellationToken cancellation, Action<string>? progress = null, bool retainUnobserved = false, Action<OperationProgress>? operationProgress = null, WorkBuffers? work = null, bool forCompletion = false, MeshDepthContext? depthContext = null);
}
