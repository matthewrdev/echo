using Echo.Contracts;
using Echo.Storage;
using Echo.Projects;

namespace Echo.Texturing;

public interface ITextureSeamLeveler
{
    TextureSeamCorrections Compute(IProjectStore store, ProjectData project, ReconstructionScene scene,
        Surface mesh, CancellationToken cancellation, Action<OperationProgress>? progress = null, WorkBuffers? work = null);
}
