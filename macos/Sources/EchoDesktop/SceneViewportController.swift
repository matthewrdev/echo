import SwiftUI

@MainActor
final class SceneViewportController: ObservableObject {
    let orientation = SceneOrientationState()
    weak var view: ArtifactSceneView?
    func orient(_ direction: SceneViewDirection) { view?.fitArtifact(direction: direction) }
    func reset() { view?.fitArtifact() }
    func moveCamera(by offset: Int) {
        view?.moveCamera(by: offset)
        if let view { view.window?.makeFirstResponder(view) }
    }
    func handleCameraKey(_ event: NSEvent) -> Bool { view?.handleCameraKey(event) ?? false }
}
