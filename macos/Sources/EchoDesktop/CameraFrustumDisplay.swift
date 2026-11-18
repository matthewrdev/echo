import SceneKit
import simd

struct CameraFrustumDisplay {
    static let normalDepth = 0.6
    static let selectedScale = 8.0

    func makeSelectedNode() -> SCNNode {
        let vertices = [
            SCNVector3Zero, SCNVector3(-1, -1, 1), SCNVector3(1, -1, 1),
            SCNVector3(1, 1, 1), SCNVector3(-1, 1, 1),
        ]
        let edges: [UInt32] = [0, 1, 0, 2, 0, 3, 0, 4, 1, 2, 2, 3, 3, 4, 4, 1]
        let geometry = SCNGeometry(
            sources: [SCNGeometrySource(vertices: vertices)],
            elements: [SCNGeometryElement(indices: edges, primitiveType: .line)])
        geometry.firstMaterial?.diffuse.contents = NSColor.systemYellow
        geometry.firstMaterial?.lightingModel = .constant
        geometry.firstMaterial?.readsFromDepthBuffer = false
        geometry.firstMaterial?.writesToDepthBuffer = false
        let node = SCNNode(geometry: geometry)
        node.name = "selected-camera-frustum"
        node.renderingOrder = 10
        return node
    }

    func update(_ node: SCNNode, frustum: CameraFrustum, artifact: SCNNode?) {
        var depth = Self.normalDepth * Self.selectedScale
        if let artifact {
            let box = artifact.boundingBox
            let span = max(box.max.x - box.min.x, max(box.max.y - box.min.y, box.max.z - box.min.z))
            depth = max(depth, span * 0.05)
        }
        // Scale about the camera apex, preserving its calibrated field of view and orientation.
        // The parent marker supplies the camera position. Reuse this geometry while browsing cameras.
        node.simdTransform = simd_float4x4(
            SIMD4(SIMD3<Float>(frustum.right * frustum.halfWidth * depth), 0),
            SIMD4(SIMD3<Float>(frustum.down * frustum.halfHeight * depth), 0),
            SIMD4(SIMD3<Float>(frustum.forward * depth), 0),
            SIMD4(0, 0, 0, 1))
    }
}
