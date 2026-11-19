namespace Echo.Pipeline;

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
