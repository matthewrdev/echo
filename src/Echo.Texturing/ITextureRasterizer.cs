using Echo.Contracts;
using Echo.Projects;
using SkiaSharp;

namespace Echo.Texturing;

public interface ITextureRasterizer
{
    double Paint(IProjectStore store, ProjectData project, ReconstructionScene scene, Surface mesh,
        TextureResult texture, IReadOnlyList<TexturePatch> patches, SKBitmap atlas, CancellationToken cancellation);
}
