import SwiftUI

@MainActor
final class SceneViewportController: ObservableObject {
    let orientation = SceneOrientationState()
    weak var view: ArtifactSceneView?
    func orient(_ direction: SceneViewDirection) { view?.fitArtifact(direction: direction) }
    func reset() { view?.fitArtifact() }
}
