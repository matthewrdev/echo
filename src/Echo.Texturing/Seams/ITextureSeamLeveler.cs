using Echo.Models;
using Echo.Projects;
using Echo.Storage.Resources;
using Echo.Storage;

namespace Echo.Texturing.Seams;

public interface ITextureSeamLeveler
{
    TextureSeamCorrections Compute(IProjectStore store, ProjectData project, ReconstructionScene scene,
        Surface mesh, CancellationToken cancellation, Action<OperationProgress>? progress = null, WorkBuffers? work = null);
}
