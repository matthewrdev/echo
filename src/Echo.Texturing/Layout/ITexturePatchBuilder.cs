using Echo.Models;
using Echo.Storage.Resources;
using Echo.Storage;

namespace Echo.Texturing.Layout;

public interface ITexturePatchBuilder
{
    IReadOnlyList<TexturePatch> Build(ProjectData project, ReconstructionScene scene, Surface mesh, CancellationToken cancellation, IReadOnlyList<bool>? vegetationFaces = null, Action<string>? progress = null, WorkBuffers? work = null, IReadOnlyList<int>? priorityBands = null, Action<OperationProgress>? operationProgress = null);
}
