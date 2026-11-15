using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Imaging;
using Echo.Projects;
using SkiaSharp;

namespace Echo.Texturing;

[Export(typeof(ITextureBaker)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class TextureBaker : ITextureBaker
{
    private readonly ITextureLayoutPlanner layoutPlanner;
    private readonly ITextureRasterizer rasterizer;
    private readonly IRasterImageService images;
    private readonly ITextureSeamLeveler seamLeveler;

    [ImportingConstructor]
    public TextureBaker(ITextureLayoutPlanner layoutPlanner, ITextureRasterizer rasterizer,
        IRasterImageService images, ITextureSeamLeveler seamLeveler)
    {
        this.layoutPlanner = layoutPlanner;
        this.rasterizer = rasterizer;
        this.images = images;
        this.seamLeveler = seamLeveler;
    }

    public TextureResult Bake(IProjectStore store, ProjectData project, ReconstructionScene scene, Surface mesh,
        string output, CancellationToken cancellation, Action<string>? progress = null, IReadOnlyList<bool>? vegetationFaces = null)
    {
        var layout = layoutPlanner.Plan(project, scene, mesh, cancellation, progress, vegetationFaces);
        var patches = layout.Patches;
        var vegetationCharts = layout.VegetationChartCount;
        var size = project.Settings.TextureSize;
        var scale = layout.TexelsPerWorldUnit;
        var surface = new Surface();
        var result = new TextureResult
        {
            Size = size,
            Surface = surface,
            MeshFile = "mesh.json",
            ChartCount = patches.Count,
            VegetationChartCount = vegetationCharts,
            VegetationTextureScale = vegetationCharts == 0 ? 1 : project.Settings.VegetationTextureScale,
            TexelsPerWorldUnit = scale,
            BaselineTexelsPerWorldUnit = layout.BaselineTexelsPerWorldUnit
        };
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
                var face = mesh.Faces[index];
                surface.Faces.Add([lookup[face[0]], lookup[face[1]], lookup[face[2]]]);
                surface.SourceImages.Add(mesh.SourceImages[index]);
            }
        }

        progress?.Invoke("Levelling exposure and colour differences along photographic seams…");
        result.Corrections = seamLeveler.Compute(store, project, scene, surface, cancellation);
        result.SeamEdges = result.Corrections.SeamEdges;
        result.MeanSeamDifferenceBefore = result.Corrections.MeanDifferenceBefore;
        result.MeanSeamDifferenceAfter = result.Corrections.MeanDifferenceAfter;
        progress?.Invoke("Baking source photographs into the packed mesh UVs…");
        using var atlas = new SKBitmap(size, size);
        result.CoverageFraction = rasterizer.Paint(store, project, scene, surface, result, patches, atlas, cancellation);
        progress?.Invoke("Encoding the photographic texture atlas…");
        images.Save(atlas, output, SKEncodedImageFormat.Jpeg, project.Settings.TextureJpegQuality);
        return result;
    }
}
