using Echo.Contracts;
using Echo.Storage;

namespace Echo.Texturing;

public interface ITexturePatchBuilder
{
    IReadOnlyList<TexturePatch> Build(ProjectData project, ReconstructionScene scene, Surface mesh, CancellationToken cancellation, IReadOnlyList<bool>? vegetationFaces = null, Action<string>? progress = null, WorkBuffers? work = null, IReadOnlyList<int>? priorityBands = null);
}
