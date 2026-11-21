using Echo.Models;

namespace Echo.Pipeline.Meshes;

internal sealed class MeshProgress
{
    private readonly Action<OperationProgress> report;
    private readonly MeshPhase[] phases;
    private readonly string[] steps;
    private int index;

    public MeshProgress(Settings settings, Action<OperationProgress> report)
    {
        this.report = report;
        phases = Enum.GetValues<MeshPhase>().Where(phase =>
            (phase != MeshPhase.OptimiseVegetation || settings.OptimiseVegetation)
            && (phase != MeshPhase.TrimIslands || settings.MeshIslandMode != IslandTrimMode.Off)).ToArray();
        steps = phases.Select(Title).ToArray();
    }

    public void Begin(MeshPhase phase)
    {
        index = Array.IndexOf(phases, phase);
        Report(new OperationProgress(steps[index], steps[index] + "…") { Steps = [] });
    }

    public void Report(OperationProgress progress)
    {
        report(progress with
        {
            StageSteps = steps,
            StageStepIndex = index
        });
    }

    private string Title(MeshPhase phase) => phase switch
    {
        MeshPhase.LoadInputs => "Load inputs",
        MeshPhase.ExtractSurface => "Extract surface",
        MeshPhase.SimplifyGeometry => "Simplify geometry",
        MeshPhase.RepairObservedHoles => "Repair observed holes",
        MeshPhase.CompleteSurface => "Complete surface",
        MeshPhase.RepairFinalHoles => "Repair final holes",
        MeshPhase.OptimiseVegetation => "Optimise vegetation",
        MeshPhase.TrimIslands => "Trim islands",
        MeshPhase.SelectPhotographs => "Select photographs",
        MeshPhase.SaveMesh => "Save mesh",
        _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };
}
