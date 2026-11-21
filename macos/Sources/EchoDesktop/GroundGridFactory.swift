import AppKit
import EchoRendering
import SceneKit

final class GroundGridFactory {
    func make(bounds: SceneBounds) -> SCNNode {
        // Limit extent, never increase cell spacing: every cell remains one scene metre.
        let radius = min(500, max(5, Int(ceil(bounds.span / 2)) + 2))
        let cx = floor(bounds.centre.x)
        let cz = floor(bounds.centre.z)
        let y = floor(bounds.minimum.y) - 0.01
        var vertices: [SCNVector3] = []
        var guides: [SCNVector3] = []
        vertices.reserveCapacity((radius * 2 + 1) * 4)
        for offset in -radius...radius {
            let step = Double(offset)
            let lines = [
                SCNVector3(step, 0, -Double(radius)), SCNVector3(step, 0, Double(radius)),
                SCNVector3(-Double(radius), 0, step), SCNVector3(Double(radius), 0, step),
            ]
            vertices.append(contentsOf: lines)
            if offset.isMultiple(of: 10) { guides.append(contentsOf: lines) }
        }
        let node = SCNNode(geometry: lines(vertices: vertices))
        node.name = "ground-grid"
        node.position = SCNVector3(cx, y, cz)
        node.isHidden = true
        let guideNode = SCNNode(geometry: lines(vertices: guides))
        guideNode.name = "ground-grid-guides"
        node.addChildNode(guideNode)
        return node
    }

    private func lines(vertices: [SCNVector3]) -> SCNGeometry {
        let geometry = SCNGeometry(
            sources: [SCNGeometrySource(vertices: vertices)],
            elements: [SCNGeometryElement(indices: Array(0..<UInt32(vertices.count)), primitiveType: .line)])
        let material = SCNMaterial()
        material.diffuse.contents = NSColor(calibratedWhite: 0.45, alpha: 1)
        material.lightingModel = .constant
        material.transparency = 0.18
        material.blendMode = .alpha
        material.writesToDepthBuffer = false
        material.isDoubleSided = true
        geometry.materials = [material]
        return geometry
    }
}
