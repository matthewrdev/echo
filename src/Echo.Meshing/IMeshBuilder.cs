using Echo.Contracts;

namespace Echo.Meshing;

public interface IMeshBuilder
{
    Surface Extract(FusedVolume volume, CancellationToken cancellation);
    Surface AssignCameras(ProjectData project,
        ReconstructionScene scene,
        IReadOnlyList<DepthMap> maps,
        Surface geometry,
CancellationToken cancellation);
}
