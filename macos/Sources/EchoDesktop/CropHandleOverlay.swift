import SceneKit

final class CropHandleOverlay {
    private weak var scene: SCNScene?
    private var region: ExclusionRegion?

    func update(scene: SCNScene?, region: ExclusionRegion?) {
        guard self.scene !== scene || self.region != region else { return }
        self.scene?.rootNode.childNode(withName: "crop-handles", recursively: false)?.removeFromParentNode()
        self.scene = scene
        self.region = region
        guard let scene, let region else { return }
        let handles = CropHandle.handles(for: region)
        guard !handles.isEmpty else { return }
        let root = SCNNode()
        root.name = "crop-handles"
        for handle in handles {
            let dots = SCNGeometryElement(data: nil, primitiveType: .point, primitiveCount: 1, bytesPerIndex: 0)
            dots.minimumPointScreenSpaceRadius = handle.kind == .move ? 8 : 6
            dots.maximumPointScreenSpaceRadius = dots.minimumPointScreenSpaceRadius
            let geometry = SCNGeometry(sources: [SCNGeometrySource(vertices: [handle.point])], elements: [dots])
            let material = SCNMaterial()
            material.diffuse.contents = handle.kind.colour
            material.lightingModel = .constant
            material.readsFromDepthBuffer = false
            material.writesToDepthBuffer = false
            geometry.materials = [material]
            let node = SCNNode(geometry: geometry)
            node.name = handle.kind.rawValue
            node.renderingOrder = handle.kind == .move ? 32 : 31
            let border = SCNGeometryElement(data: nil, primitiveType: .point, primitiveCount: 1, bytesPerIndex: 0)
            border.minimumPointScreenSpaceRadius = dots.minimumPointScreenSpaceRadius + 2
            border.maximumPointScreenSpaceRadius = border.minimumPointScreenSpaceRadius
            let borderGeometry = SCNGeometry(sources: geometry.sources, elements: [border])
            let borderMaterial = SCNMaterial()
            borderMaterial.diffuse.contents = NSColor.black.withAlphaComponent(0.75)
            borderMaterial.lightingModel = .constant
            borderMaterial.readsFromDepthBuffer = false
            borderMaterial.writesToDepthBuffer = false
            borderGeometry.materials = [borderMaterial]
            let borderNode = SCNNode(geometry: borderGeometry)
            borderNode.renderingOrder = 30
            node.addChildNode(borderNode)
            root.addChildNode(node)
            if handle.kind == .rotate {
                let corner = SIMD3(region.width / 2, 0, region.depth / 2)
                let start = SIMD3(region.centreX, region.centreY, region.centreZ) + region.orientation.act(corner)
                let line = SCNGeometry(
                    sources: [SCNGeometrySource(vertices: [SCNVector3(start.x, start.y, start.z), handle.point])],
                    elements: [SCNGeometryElement(indices: [UInt32(0), 1], primitiveType: .line)])
                line.materials = [material]
                let stem = SCNNode(geometry: line)
                stem.renderingOrder = 30
                root.addChildNode(stem)
            }
        }
        scene.rootNode.addChildNode(root)
    }
}
