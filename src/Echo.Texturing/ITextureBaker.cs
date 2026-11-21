using Echo.Models;
using Echo.Storage;
using Echo.Projects;

namespace Echo.Texturing;

public interface ITextureBaker
{
    TextureResult Bake(IProjectStore store,
        ProjectData project,
        ReconstructionScene scene,
        Surface mesh,
        string output,
CancellationToken cancellation, Action<string>? progress = null, IReadOnlyList<bool>? vegetationFaces = null, Action<OperationProgress>? operationProgress = null, WorkBuffers? work = null);
}
