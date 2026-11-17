import SceneKit
import simd

struct SourceCameraViewpoint {
    func make(frustum: CameraFrustum, extent: SCNVector3) -> SCNNode {
        let node = SCNNode()
        node.name = "source-camera-preview"
        node.camera = SCNCamera()
        update(node, frustum: frustum, extent: extent)
        return node
    }

    func update(_ node: SCNNode, frustum: CameraFrustum, extent: SCNVector3) {
        // Reconstruction cameras use right/down/forward; SceneKit uses right/up/backward.
        node.simdTransform = simd_float4x4(
            SIMD4(SIMD3<Float>(frustum.right), 0),
            SIMD4(-SIMD3<Float>(frustum.down), 0),
            SIMD4(-SIMD3<Float>(frustum.forward), 0),
            SIMD4(SIMD3<Float>(frustum.centre), 1))
        guard let camera = node.camera else { return }
        camera.projectionDirection = .vertical
        camera.fieldOfView = CGFloat(2 * atan(frustum.halfHeight) * 180 / .pi)
        camera.zNear = 0.0001
        camera.zFar = max(1, simd_length(SIMD3<Double>(extent.x, extent.y, extent.z)) * 2)
    }
}
