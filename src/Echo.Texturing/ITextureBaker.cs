using Echo.Geometry;
using Echo.Models;
using Echo.Texturing.Layout;
using Echo.Projects;
using Echo.Storage.Resources;
using Echo.Storage;

namespace Echo.Texturing;

public interface ITextureBaker
{
    TextureResult Bake(IProjectStore store,
        ProjectData project,
        ReconstructionScene scene,
        Surface mesh,
        string output,
        CancellationToken cancellation, Action<string>? progress = null, IReadOnlyList<bool>? vegetationFaces = null, Action<OperationProgress>? operationProgress = null, WorkBuffers? work = null, TextureLayout? preparedLayout = null,
        Func<int, V3>? correctionForCorner = null, bool writeAtlasVariants = true);
}
