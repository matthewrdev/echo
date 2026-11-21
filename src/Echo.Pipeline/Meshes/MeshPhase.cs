namespace Echo.Pipeline.Meshes;

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
