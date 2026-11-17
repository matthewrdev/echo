using System.ComponentModel.Composition;
using Echo.Contracts;
using Echo.Storage;

namespace Echo.Texturing;

[Export(typeof(ITextureLayoutPlanner)), PartCreationPolicy(CreationPolicy.Shared)]
public sealed class TextureLayoutPlanner : ITextureLayoutPlanner
{
    private readonly ITexturePatchBuilder builder;
    private readonly ITexturePatchPacker packer;

    [ImportingConstructor]
    public TextureLayoutPlanner(ITexturePatchBuilder builder, ITexturePatchPacker packer)
    {
        this.builder = builder;
        this.packer = packer;
    }

    public TextureLayout Plan(ProjectData project, ReconstructionScene scene, Surface mesh,
        CancellationToken cancellation, Action<string>? progress = null, IReadOnlyList<bool>? vegetationFaces = null, WorkBuffers? work = null)
    {
        if (vegetationFaces != null && vegetationFaces.Count != mesh.Faces.Count)
        {
            throw new ArgumentException("Expected one classification per surface face.", nameof(vegetationFaces));
        }
        progress?.Invoke("Unwrapping and packing connected surface charts…");
        var resolution = new TextureResolutionPlan(project.Settings.TextureSize, project.Settings.TextureGutter);
        var priorities = TexturePriorityBudget.Classify(mesh, project.Settings.TexturePriority, cancellation);
        if (priorities != null)
        {
            progress?.Invoke($"Applying painted texture priorities to {priorities.Count(band => band != 0):N0} surface faces…");
        }
        var patches = builder.Build(project, scene, mesh, cancellation, progress: progress, work: work, priorityBands: priorities);
        TexturePriorityBudget.Apply(patches, priorities);
        var baseline = packer.Pack(patches, resolution.MinimumSize, resolution.MinimumGutter, cancellation, progress, project.Settings.ComputeBackend);
        var best = new TextureLayout { Patches = patches, TexelsPerWorldUnit = baseline, BaselineTexelsPerWorldUnit = baseline };
        var optimise = vegetationFaces != null && vegetationFaces.Any(value => value) && project.Settings.VegetationTextureScale < 1;
        if (!optimise)
        {
            return Scale(best, resolution);
        }
        // Cutting many tiny vegetation islands can spend more on gutters than
        // it saves in texels. Measure both layouts against the unchanged layout;
        // never trade away rock density merely to reduce vegetation charts.
        for (var split = 0; split < 2; split++)
        {
            progress?.Invoke(split == 0 ? "Comparing vegetation texture budgets on existing charts…"
                : "Comparing separate vegetation charts, including their padding cost…");
            var candidate = split == 0 ? Copy(patches)
                : builder.Build(project, scene, mesh, cancellation, vegetationFaces, progress, work, priorities);
            if (split != 0)
            {
                TexturePriorityBudget.Apply(candidate, priorities);
            }
            var count = TextureVegetationBudget.Apply(candidate, vegetationFaces, project.Settings.VegetationTextureScale);
            if (count == 0)
            {
                continue;
            }
            var scale = packer.TryPack(candidate, resolution.MinimumSize, resolution.MinimumGutter, cancellation, progress, project.Settings.ComputeBackend);
            if (scale.HasValue && scale.Value >= best.TexelsPerWorldUnit)
            {
                best = new TextureLayout
                {
                    Patches = candidate,
                    TexelsPerWorldUnit = scale.Value,
                    BaselineTexelsPerWorldUnit = baseline,
                    VegetationChartCount = count
                };
            }
        }
        progress?.Invoke($"Vegetation budget: {best.VegetationChartCount:N0} reduced charts; rock texel density {100 * (best.TexelsPerWorldUnit / baseline - 1):F2}% above the unchanged layout.");
        return Scale(best, resolution);
    }

    private TextureLayout Scale(TextureLayout layout, TextureResolutionPlan resolution)
    {
        foreach (var patch in layout.Patches)
        {
            var bounds = patch.AtlasBounds;
            patch.AtlasBounds = new SkiaSharp.SKRect((float)(bounds.Left * resolution.Scale),
                (float)(bounds.Top * resolution.Scale), (float)(bounds.Right * resolution.Scale),
                (float)(bounds.Bottom * resolution.Scale));
        }
        return new TextureLayout
        {
            Patches = layout.Patches,
            TexelsPerWorldUnit = layout.TexelsPerWorldUnit * resolution.Scale,
            BaselineTexelsPerWorldUnit = layout.BaselineTexelsPerWorldUnit * resolution.Scale,
            VegetationChartCount = layout.VegetationChartCount,
            MinimumAtlasSize = resolution.MinimumSize,
            GutterPixels = resolution.Gutter
        };
    }

    private IReadOnlyList<TexturePatch> Copy(IReadOnlyList<TexturePatch> patches)
    {
        return patches.Select(patch =>
        {
            var copy = new TexturePatch { Bounds = patch.Bounds };
            copy.Faces.AddRange(patch.Faces);
            copy.Triangles.AddRange(patch.Triangles);
            foreach (var entry in patch.Coordinates)
            {
                copy.Coordinates.Add(entry.Key, (double[])entry.Value.Clone());
            }
            return copy;
        }).ToArray();
    }

}
