namespace Echo.Meshing.Photographs;

internal enum PhotographSelectionPhase
{
    BuildVisibilityIndex,
    CheckMeasuredVisibility,
    CheckCompletedGaps,
    BuildSeamNeighbours,
    ChooseCoherentPhotographs,
    AssembleMesh
}
