import SceneKit
import simd

@MainActor
final class ArtifactOrbitInteraction {
    private(set) var active = false
    private var previous = NSPoint.zero
    private var pivot = SIMD3<Float>.zero
    private var cameraControlEnabled = false

    func begin(at location: NSPoint, in view: SCNView) -> Bool {
        guard let camera = view.pointOfView,
            let artifact = view.scene?.rootNode.childNode(withName: "artifact", recursively: false),
            let elements = artifact.geometry?.elements
        else { return false }
        let isMesh = elements.contains { $0.primitiveType == .triangles }
        let isCloud = elements.contains { $0.primitiveType == .point }
        guard isMesh || isCloud else { return false }
        let controller = view.defaultCameraController
        controller.stopInertia()
        controller.pointOfView = camera
        controller.automaticTarget = false
        if isCloud {
            // Pick once at drag start, using only saved cloud geometry. Camera
            // frustums, the grid and selection markers must never become pivots.
            if let point = CloudPointPicker().pick(at: location, in: view, node: artifact, preferFrontmost: true) {
                controller.target = point
            }
        } else if let hit = view.hitTest(location, options: [.rootNode: artifact, .backFaceCulling: false]).first {
            controller.target = hit.worldCoordinates
        }
        let target = controller.target
        pivot = SIMD3(Float(target.x), Float(target.y), Float(target.z))
        previous = location
        cameraControlEnabled = view.allowsCameraControl
        view.allowsCameraControl = false
        active = true
        return true
    }

    func drag(to location: NSPoint, in view: SCNView) {
        guard active, let camera = view.pointOfView else { return }
        let dx = Float(location.x - previous.x)
        let dy = Float(location.y - previous.y) * (view.isFlipped ? -1 : 1)
        previous = location
        let yaw = simd_quatf(angle: -dx * 0.005, axis: SIMD3(0, 1, 0))
        let elevation = asin(max(-1, min(1, camera.simdWorldFront.y)))
        let pitchAngle = max(-1.55 - elevation, min(1.55 - elevation, dy * 0.005))
        let pitch = simd_quatf(angle: pitchAngle, axis: yaw.act(camera.simdWorldRight))
        let rotation = pitch * yaw
        // Rotate both the camera position and its basis around the picked point.
        // Retargeting must not aim the camera afresh and jump the model on screen.
        var transform = simd_float4x4(rotation * camera.simdWorldOrientation)
        transform.columns.3 = SIMD4(pivot + rotation.act(camera.simdWorldPosition - pivot), 1)
        camera.simdWorldTransform = transform
    }

    func end(in view: SCNView) {
        guard active else { return }
        active = false
        view.allowsCameraControl = cameraControlEnabled
    }
}
