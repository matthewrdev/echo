using Echo.Contracts;

namespace Echo.Pipeline;

internal sealed class AlignmentProgress
{
    private readonly Action<OperationProgress> report;
    private readonly AlignmentPhase[] phases;
    private readonly string[] steps;
    private int index;

    public AlignmentProgress(bool guidedPass, Action<OperationProgress> report)
    {
        this.report = report;
        phases = Enum.GetValues<AlignmentPhase>().Where(phase =>
        {
            var recoveryPhase = phase >= AlignmentPhase.GuidedMatching && phase <= AlignmentPhase.AssessRecovery;
            return guidedPass || !recoveryPhase;
        }).ToArray();
        steps = phases.Select(Title).ToArray();
    }

    public void Begin(AlignmentPhase phase, string message)
    {
        index = Array.IndexOf(phases, phase);
        Report(new OperationProgress(steps[index], message) { Steps = [] });
    }

    public void Report(OperationProgress progress)
    {
        report(progress with
        {
            StageSteps = steps,
            StageStepIndex = index
        });
    }

    private string Title(AlignmentPhase phase) => phase switch
    {
        AlignmentPhase.LoadMatches => "Load matches",
        AlignmentPhase.InitialSolve => "Initial alignment",
        AlignmentPhase.SaveInitial => "Save initial alignment",
        AlignmentPhase.GuidedMatching => "Guided matching",
        AlignmentPhase.ValidateRecovery => "Validate recovery",
        AlignmentPhase.RefineRecovery => "Refine recovered alignment",
        AlignmentPhase.AssessRecovery => "Check recovery quality",
        AlignmentPhase.SaveAlignment => "Save alignment",
        _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };
}
