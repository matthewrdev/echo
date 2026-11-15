using System.ComponentModel.Composition;
using Echo.Contracts;

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
        CancellationToken cancellation, Action<string>? progress = null, IReadOnlyList<bool>? vegetationFaces = null)
    {
        if (vegetationFaces != null && vegetationFaces.Count != mesh.Faces.Count)
        {
            throw new ArgumentException("Expected one classification per surface face.", nameof(vegetationFaces));
        }
        progress?.Invoke("Unwrapping and packing connected surface charts…");
        var patches = builder.Build(project, scene, mesh, cancellation);
        var baseline = packer.Pack(patches, project.Settings.TextureSize, project.Settings.TextureGutter, cancellation);
        var best = new TextureLayout { Patches = patches, TexelsPerWorldUnit = baseline, BaselineTexelsPerWorldUnit = baseline };
        var optimise = vegetationFaces != null && vegetationFaces.Any(value => value) && project.Settings.VegetationTextureScale < 1;
        if (!optimise)
        {
            return best;
        }
        // Cutting many tiny vegetation islands can spend more on gutters than
        // it saves in texels. Measure both layouts against the unchanged layout;
        // never trade away rock density merely to reduce vegetation charts.
        for (var split = 0; split < 2; split++)
        {
            progress?.Invoke(split == 0 ? "Comparing vegetation texture budgets on existing charts…"
                : "Comparing separate vegetation charts, including their padding cost…");
            var candidate = builder.Build(project, scene, mesh, cancellation, split == 1 ? vegetationFaces : null);
            var count = TextureVegetationBudget.Apply(candidate, vegetationFaces, project.Settings.VegetationTextureScale);
            if (count == 0)
            {
                continue;
            }
            var scale = packer.TryPack(candidate, project.Settings.TextureSize, project.Settings.TextureGutter, cancellation);
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
        return best;
    }
}
