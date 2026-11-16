import SceneKit

struct GeoreferenceArtifact {
    let reconstruction: ReconstructionScene
    let presentation: ArtifactPresentation
    let overlay: ModelMapOverlay?
    let cameras: [MapCamera]
    let bounds: SceneBounds
}
