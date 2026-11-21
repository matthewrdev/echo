using Echo.Models;

namespace Echo.Pipeline.Texture;

internal sealed class TextureProgress
{
    private readonly Action<OperationProgress> report;
    private readonly string[] steps;
    private int index;

    public TextureProgress(Settings settings, Action<OperationProgress> report)
    {
        this.report = report;
        var plan = new List<string> { "Load inputs", "UV charts", "Atlas packing" };
        if (settings.OptimiseVegetation && settings.VegetationTextureScale < 1)
        {
            plan.Add("Vegetation budget");
        }

        plan.AddRange(["UV mesh", "Seam indexing", "Seam sampling", "Exposure correction", "Local seam correction",
            "Texture projection", "Inferred texture completion"]);
        if (settings.TextureSeamBlendPixels > 0)
        {
            plan.Add("Seam softening");
        }

        plan.AddRange(["Atlas gutters", "Atlas background", "Atlas pixels"]);
        if (settings.TextureColourEnhancement > 0)
        {
            plan.Add("Colour enhancement");
        }

        if (settings.ColourGrade is { IsIdentity: false })
        {
            plan.Add("Colour grading");
        }

        plan.AddRange(["Atlas encoding", "Atlas variants", "Save texture"]);
        steps = plan.ToArray();
    }

    public void Report(OperationProgress progress)
    {
        var next = Array.IndexOf(steps, progress.Operation);
        if (next >= 0)
        {
            index = next;
        }

        report(progress with
        {
            StageSteps = steps,
            StageStepIndex = index
        });
    }
}
