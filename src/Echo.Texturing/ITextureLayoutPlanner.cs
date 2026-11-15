using Echo.Contracts;

namespace Echo.Texturing;

public interface ITextureLayoutPlanner
{
    TextureLayout Plan(ProjectData project, ReconstructionScene scene, Surface mesh,
        CancellationToken cancellation, Action<string>? progress = null, IReadOnlyList<bool>? vegetationFaces = null);
}
