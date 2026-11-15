using Echo.Contracts;
using Echo.Projects;

namespace Echo.Texturing;

public interface ITextureSeamLeveler
{
    TextureSeamCorrections Compute(IProjectStore store, ProjectData project, ReconstructionScene scene,
        Surface mesh, CancellationToken cancellation);
}
