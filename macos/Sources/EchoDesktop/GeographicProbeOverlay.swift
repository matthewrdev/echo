import EchoRendering
import SceneKit

struct GeographicProbeOverlay {
    func update(scene: SCNScene, result: GeographicProbeResult?) {
        scene.rootNode.childNode(withName: "geographic-test-location", recursively: false)?.removeFromParentNode()
        guard let result else { return }
        let root = SCNNode()
        root.name = "geographic-test-location"
        let points = SCNGeometryElement(data: nil, primitiveType: .point, primitiveCount: 1, bytesPerIndex: 0)
        points.minimumPointScreenSpaceRadius = 8
        points.maximumPointScreenSpaceRadius = 8
        let marker = SCNGeometry(sources: [SCNGeometrySource(vertices: [result.local])], elements: [points])
        let material = SCNMaterial()
        material.diffuse.contents = NSColor.systemPink
        material.lightingModel = .constant
        material.readsFromDepthBuffer = false
        material.writesToDepthBuffer = false
        marker.materials = [material]
        let point = SCNNode(geometry: marker)
        point.renderingOrder = 20
        root.addChildNode(point)
        if result.accuracy > 0 {
            let vertices = (0..<96).map { index in
                let angle = Double(index) * 2 * .pi / 96
                return SCNVector3(
                    result.local.x + cos(angle) * result.accuracy, result.local.y,
                    result.local.z + sin(angle) * result.accuracy)
            }
            let indices = (0..<UInt32(96)).flatMap { [$0, ($0 + 1) % 96] }
            let ring = SCNGeometry(
                sources: [SCNGeometrySource(vertices: vertices)],
                elements: [SCNGeometryElement(indices: indices, primitiveType: .line)])
            ring.materials = [material]
            let circle = SCNNode(geometry: ring)
            circle.renderingOrder = 19
            root.addChildNode(circle)
        }
        scene.rootNode.addChildNode(root)
    }
}
