import SceneKit
import SwiftUI

@MainActor
final class SceneViewportController: ObservableObject {
    let orientation = SceneOrientationState()
    weak var view: ArtifactSceneView?
    func orient(_ direction: SceneViewDirection) { view?.fitArtifact(direction: direction) }
    func reset() { view?.fitArtifact() }
    func focus(on point: SCNVector3, radius: Double) {
        guard let view, let camera = view.pointOfView else { return }
        let bounds = SceneBounds(positions: [
            [point.x - radius, point.y - radius, point.z - radius],
            [point.x + radius, point.y + radius, point.z + radius],
        ])
        let front = camera.worldFront
        view.defaultCameraController.stopInertia()
        bounds.fit(
            camera: camera, size: view.bounds.size, offset: SCNVector3(-front.x, -front.y, -front.z), up: camera.worldUp
        )
        view.defaultCameraController.target = point
        view.needsInitialFit = false
    }
    func moveCamera(by offset: Int) {
        view?.moveCamera(by: offset)
        if let view { view.window?.makeFirstResponder(view) }
    }
    func handleCameraKey(_ event: NSEvent) -> Bool { view?.handleCameraKey(event) ?? false }
}
