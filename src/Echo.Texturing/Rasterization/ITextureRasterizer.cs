using Echo.Models;
using Echo.Projects;
using Echo.Storage.Resources;
using Echo.Storage;
using Echo.Texturing.Layout;
using SkiaSharp;

namespace Echo.Texturing.Rasterization;

public interface ITextureRasterizer
{
    double Paint(IProjectStore store, ProjectData project, ReconstructionScene scene, Surface mesh,
        TextureResult texture, IReadOnlyList<TexturePatch> patches, SKBitmap atlas, CancellationToken cancellation, Action<OperationProgress>? progress = null, WorkBuffers? work = null);
}
