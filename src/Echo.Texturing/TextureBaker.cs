using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Imaging;
using Echo.Projects;
using SkiaSharp;

namespace Echo.Texturing;

[Export(typeof(ITextureBaker)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class TextureBaker : ITextureBaker
{
    private readonly ITexturePatchBuilder patchBuilder;
    private readonly ITexturePatchPacker patchPacker;
    private readonly ITextureRasterizer rasterizer;
    private readonly IRasterImageService images;

    [ImportingConstructor]
    public TextureBaker(ITexturePatchBuilder patchBuilder, ITexturePatchPacker patchPacker, ITextureRasterizer rasterizer, IRasterImageService images)
    {
        this.patchBuilder = patchBuilder;
        this.patchPacker = patchPacker;
        this.rasterizer = rasterizer;
        this.images = images;
    }

    public TextureResult Bake(IProjectStore store, ProjectData project, ReconstructionScene scene, Surface mesh,
        string output, CancellationToken cancellation, Action<string>? progress = null)
    {
        progress?.Invoke("Unwrapping connected surface charts…");
        var patches = patchBuilder.Build(project, scene, mesh, cancellation);
        var size = project.Settings.TextureSize;
        progress?.Invoke($"Packing {patches.Count:N0} UV islands with rotation and padding…");
        var scale = patchPacker.Pack(patches, size, project.Settings.TextureGutter, cancellation);
        var surface = new Surface();
        var result = new TextureResult { Size = size, Surface = surface, MeshFile = "mesh.json", ChartCount = patches.Count };
        foreach (var patch in patches)
        {
            cancellation.ThrowIfCancellationRequested();
            var lookup = new Dictionary<int, int>();
            foreach (var vertex in patch.Coordinates)
            {
                lookup.Add(vertex.Key, surface.Vertices.Count);
                surface.Vertices.Add(mesh.Vertices[vertex.Key]);
                var point = patch.Coordinate(vertex.Key, patch.QuarterTurns);
                result.Uvs.Add([(patch.AtlasBounds.Left + point.X * scale) / size,
                    (patch.AtlasBounds.Top + point.Y * scale) / size]);
            }

            foreach (var index in patch.Faces)
            {
                surface.Faces.Add(mesh.Faces[index].Select(v => lookup[v]).ToArray());
                surface.SourceImages.Add(mesh.SourceImages[index]);
            }
        }

        progress?.Invoke("Baking source photographs into the packed mesh UVs…");
        using var atlas = new SKBitmap(size, size);
        result.CoverageFraction = rasterizer.Paint(store, project, scene, surface, result, patches, atlas, cancellation);
        progress?.Invoke("Encoding the photographic texture atlas…");
        images.Save(atlas, output, SKEncodedImageFormat.Jpeg, project.Settings.TextureJpegQuality);
        return result;
    }
}
