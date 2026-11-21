namespace Echo.Pipeline.Alignment;

internal enum AlignmentPhase
{
    LoadMatches,
    InitialSolve,
    SaveInitial,
    GuidedMatching,
    ValidateRecovery,
    RefineRecovery,
    AssessRecovery,
    SaveAlignment
}
