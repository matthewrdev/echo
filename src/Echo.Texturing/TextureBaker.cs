using Echo.Imaging;
using Echo.Models;
using Echo.Projects;
using Echo.Storage.Records;
using Echo.Storage.Resources;
using Echo.Storage;
using Echo.Texturing.Layout;
using Echo.Texturing.Rasterization;
using Echo.Texturing.Seams;
using SkiaSharp;

namespace Echo.Texturing;

public sealed class TextureBaker : ITextureBaker
{
    private readonly ITextureLayoutPlanner layoutPlanner;
    private readonly ITextureRasterizer rasterizer;
    private readonly IRasterImageService images;
    private readonly ITextureSeamLeveler seamLeveler;

    public TextureBaker(ITextureLayoutPlanner layoutPlanner, ITextureRasterizer rasterizer,
        IRasterImageService images, ITextureSeamLeveler seamLeveler)
    {
        this.layoutPlanner = layoutPlanner;
        this.rasterizer = rasterizer;
        this.images = images;
        this.seamLeveler = seamLeveler;
    }

    public TextureResult Bake(IProjectStore store, ProjectData project, ReconstructionScene scene, Surface mesh,
        string output, CancellationToken cancellation, Action<string>? progress = null, IReadOnlyList<bool>? vegetationFaces = null, Action<OperationProgress>? operationProgress = null, WorkBuffers? work = null, TextureLayout? preparedLayout = null)
    {
        var layout = preparedLayout ?? layoutPlanner.Plan(project, scene, mesh, cancellation, progress, vegetationFaces, work, operationProgress);
        var patches = layout.Patches;
        var vegetationCharts = layout.VegetationChartCount;
        var size = project.Settings.TextureSize;
        var scale = layout.TexelsPerWorldUnit;
        var surface = work?.Surface() ?? new Surface();
        var result = new TextureResult
        {
            Uvs = work?.List(new UvRecordCodec()) ?? [],
            Size = size,
            MinimumAtlasSize = layout.MinimumAtlasSize,
            GutterPixels = layout.GutterPixels,
            Surface = surface,
            MeshFile = "mesh.json",
            ChartCount = patches.Count,
            VegetationChartCount = vegetationCharts,
            VegetationTextureScale = vegetationCharts == 0 ? 1 : project.Settings.VegetationTextureScale,
            TexelsPerWorldUnit = scale,
            BaselineTexelsPerWorldUnit = layout.BaselineTexelsPerWorldUnit
        };
        operationProgress?.Invoke(new OperationProgress("UV mesh", "Building the textured mesh and UV coordinates…", 0, patches.Count, "charts"));
        var completedCharts = 0;
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

            completedCharts++;
            if (completedCharts % 1000 == 0 || completedCharts == patches.Count)
            {
                operationProgress?.Invoke(new OperationProgress("UV mesh", "Building the textured mesh and UV coordinates…", completedCharts, patches.Count, "charts"));
            }
        }

        progress?.Invoke("Levelling exposure and colour differences along photographic seams…");
        result.Corrections = seamLeveler.Compute(store, project, scene, surface, cancellation, operationProgress, work);
        result.SeamEdges = result.Corrections.SeamEdges;
        result.MeanSeamDifferenceBefore = result.Corrections.MeanDifferenceBefore;
        result.MeanSeamDifferenceAfter = result.Corrections.MeanDifferenceAfter;
        progress?.Invoke("Baking source photographs into the packed mesh UVs…");
        using var atlas = new SKBitmap(size, size);
        result.CoverageFraction = rasterizer.Paint(store, project, scene, surface, result, patches, atlas, cancellation, operationProgress, work);
        if (project.Settings.TextureColourEnhancement > 0)
        {
            operationProgress?.Invoke(new OperationProgress("Colour enhancement", "Finishing texture contrast and colour…"));
            progress?.Invoke("Finishing texture contrast and colour…");
            TextureColourEnhancer.Apply(atlas, project.Settings.TextureColourEnhancement, cancellation);
        }

        if (project.Settings.ColourGrade is { IsIdentity: false } grade)
        {
            operationProgress?.Invoke(new OperationProgress("Colour grading", "Applying saved colour grade…"));
            progress?.Invoke("Applying saved colour grade…");
            new ColourGradeTransform(grade).Apply(atlas, cancellation);
            result.ColourGrade = grade;
        }

        operationProgress?.Invoke(new OperationProgress("Atlas encoding", "Encoding the photographic texture atlas…", 0, 1, "atlases"));
        progress?.Invoke("Encoding the photographic texture atlas…");
        images.Save(atlas, output, SKEncodedImageFormat.Jpeg, project.Settings.TextureJpegQuality);
        operationProgress?.Invoke(new OperationProgress("Atlas encoding", "Photographic atlas encoded.", 1, 1, "atlases"));
        SaveVariants(atlas, result, output, project.Settings.TextureJpegQuality, cancellation, progress, operationProgress);
        return result;
    }

    private void SaveVariants(SKBitmap atlas, TextureResult result, string output, int quality,
        CancellationToken cancellation, Action<string>? progress, Action<OperationProgress>? operationProgress)
    {
        string[] steps = ["Filter atlas", "Encode variant"];
        operationProgress?.Invoke(new OperationProgress("Atlas variants", "Preparing smaller atlases…") { Steps = steps, StepIndex = 0 });
        SKBitmap? previous = null;
        try
        {
            for (var size = result.Size / 2; size >= result.MinimumAtlasSize; size /= 2)
            {
                cancellation.ThrowIfCancellationRequested();
                progress?.Invoke($"Filtering {size:N0} × {size:N0} atlas with shared UVs…");
                operationProgress?.Invoke(new OperationProgress("Atlas variants", $"Filtering {size:N0} × {size:N0} atlas…") { Steps = steps, StepIndex = 0 });
                var reduced = TextureAtlasDownsampler.Half(previous ?? atlas, cancellation);
                previous?.Dispose();
                previous = reduced;
                var filename = Path.GetFileNameWithoutExtension(output) + $"-{size}.jpg";
                operationProgress?.Invoke(new OperationProgress("Atlas variants", $"Encoding {size:N0} × {size:N0} atlas…") { Steps = steps, StepIndex = 1 });
                images.Save(reduced, Path.Combine(Path.GetDirectoryName(output) ?? ".", filename), SKEncodedImageFormat.Jpeg, quality);
                result.Variants.Add(new TextureAtlasVariant
                {
                    Size = size,
                    GutterPixels = result.GutterPixels * size / result.Size,
                    File = filename
                });
            }
        }
        finally
        {
            previous?.Dispose();
        }
    }

}
