namespace Echo.Pipeline;

internal enum MeshPhase
{
    LoadInputs,
    ExtractSurface,
    SimplifyGeometry,
    RepairObservedHoles,
    CompleteSurface,
    RepairFinalHoles,
    OptimiseVegetation,
    TrimIslands,
    SelectPhotographs,
    SaveMesh
}
