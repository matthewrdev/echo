namespace Echo.Meshing;

internal enum PhotographSelectionPhase
{
    BuildVisibilityIndex,
    CheckMeasuredVisibility,
    CheckCompletedGaps,
    BuildSeamNeighbours,
    ChooseCoherentPhotographs,
    AssembleMesh
}
