import SceneKit

final class SceneViewportRenderer: NSObject, SCNSceneRendererDelegate {
    let gridVisibility = GroundGridVisibility()
    weak var orientation: SceneOrientationState?
    private var previousFrame: SceneOrientationFrame?

    func renderer(_ renderer: SCNSceneRenderer, updateAtTime time: TimeInterval) {
        gridVisibility.renderer(renderer, updateAtTime: time)
    }

    func renderer(_ renderer: SCNSceneRenderer, willRenderScene scene: SCNScene, atTime time: TimeInterval) {
        guard let camera = renderer.pointOfView?.presentation else { return }
        let frame = SceneOrientationFrame(camera: camera)
        guard frame != previousFrame else { return }
        previousFrame = frame
        // Read the camera used by this rendered frame, including orbit inertia.
        // Updating only this small observable avoids rebuilding the point-cloud view.
        DispatchQueue.main.async { [weak orientation] in orientation?.frame = frame }
    }
}
