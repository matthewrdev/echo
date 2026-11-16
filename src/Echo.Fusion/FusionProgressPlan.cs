using Echo.Contracts;

namespace Echo.Fusion;

public static class FusionProgressPlan
{
    public static string Title(FusionPhase phase) => phase switch
    {
        FusionPhase.Coverage => "Build coverage",
        FusionPhase.Merge => "Merge coverage",
        FusionPhase.HaloExpansion => "Expand neighbours",
        FusionPhase.HaloCompletion => "Complete neighbours",
        FusionPhase.GridStorage => "Store grid",
        FusionPhase.DepthPreparation => "Prepare depths",
        FusionPhase.Integration => "Integrate volume",
        FusionPhase.Cleanup => "Clean up islands",
        FusionPhase.Save => "Save volume",
        _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };

    public static OperationProgress Create(FusionPhase phase, bool cleanup, string message,
        long? completed = null, long? total = null, string unit = "items")
    {
        var phases = Enum.GetValues<FusionPhase>().Where(value => cleanup || value != FusionPhase.Cleanup).ToArray();
        return new OperationProgress(Title(phase), message, completed, total, unit)
        {
            Steps = phases.Select(Title).ToArray(),
            StepIndex = Array.IndexOf(phases, phase)
        };
    }
}
