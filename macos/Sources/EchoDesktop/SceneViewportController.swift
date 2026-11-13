import SwiftUI

@MainActor
final class SceneViewportController: ObservableObject {
    weak var view: ArtifactSceneView?
    func reset() { view?.fitArtifact() }
}
