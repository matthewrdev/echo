using Echo.Contracts;

namespace Echo.Meshing;

public interface IMeshBuilder
{
    Surface Extract(FusedVolume volume, CancellationToken cancellation);
    void FillHoles(ProjectData project, ReconstructionScene scene, IReadOnlyList<DepthMap> maps,
        Surface geometry, double voxelSize, CancellationToken cancellation);
    Surface AssignCameras(ProjectData project,
        ReconstructionScene scene,
        IReadOnlyList<DepthMap> maps,
        Surface geometry,
        CancellationToken cancellation, Action<string>? progress = null, bool retainUnobserved = false);
}
