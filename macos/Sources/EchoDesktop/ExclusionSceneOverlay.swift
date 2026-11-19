import SceneKit

@MainActor
final class ExclusionSceneOverlay {
    private weak var scene: SCNScene?
    private var root: SCNNode?
    private var regions: [ExclusionRegion] = []
    private var selectedID: String?

    func update(scene: SCNScene?, regions: [ExclusionRegion], selectedID: String?) {
        guard self.scene !== scene || self.regions != regions || self.selectedID != selectedID else { return }
        SCNTransaction.lock()
        SCNTransaction.begin()
        SCNTransaction.disableActions = true
        defer {
            SCNTransaction.commit()
            SCNTransaction.unlock()
        }
        root?.removeFromParentNode()
        root = nil
        self.scene = scene
        self.regions = regions
        self.selectedID = selectedID
        guard let scene, !regions.isEmpty else { return }
        let root = SCNNode()
        root.name = "exclusion-regions"
        for region in regions where region.enabled && region.isValid {
            let geometry: SCNGeometry
            if let hull = region.hull, region.shape == .projectedHull {
                geometry = CropHullSceneGeometry().make(hull, outline: false)
            } else if region.shape == .box {
                geometry = SCNBox(width: region.width, height: region.height, length: region.depth, chamferRadius: 0)
            } else {
                geometry = SCNCylinder(radius: region.radius, height: region.height)
            }
            let material = SCNMaterial()
            material.diffuse.contents = region.mode == .include ? NSColor.systemGreen : NSColor.systemRed
            material.lightingModel = .constant
            material.isDoubleSided = true
            material.writesToDepthBuffer = false
            material.readsFromDepthBuffer = false
            geometry.materials = [material]
            let node = SCNNode(geometry: geometry)
            node.name = region.id
            node.position = SCNVector3(region.centreX, region.centreY, region.centreZ)
            node.simdOrientation = simd_quatf(vector: SIMD4<Float>(region.orientation.vector))
            node.opacity = region.id == selectedID ? 0.24 : 0.12
            node.renderingOrder = 20
            root.addChildNode(node)
            let outline = SCNNode(geometry: outlineGeometry(region))
            outline.geometry?.materials = [material]
            outline.position = node.position
            outline.simdOrientation = node.simdOrientation
            outline.renderingOrder = 21
            root.addChildNode(outline)
        }
        scene.rootNode.addChildNode(root)
        self.root = root
    }

    private func outlineGeometry(_ region: ExclusionRegion) -> SCNGeometry {
        if let hull = region.hull, region.shape == .projectedHull {
            return CropHullSceneGeometry().make(hull, outline: true)
        }
        var vertices: [SCNVector3] = []
        var edges: [UInt32] = []
        if region.shape == .box {
            for y in [-region.height / 2, region.height / 2] {
                vertices += [
                    SCNVector3(-region.width / 2, y, -region.depth / 2),
                    SCNVector3(region.width / 2, y, -region.depth / 2),
                    SCNVector3(region.width / 2, y, region.depth / 2),
                    SCNVector3(-region.width / 2, y, region.depth / 2),
                ]
            }
            edges = [0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7]
        } else {
            let segments: UInt32 = 64
            for ring: UInt32 in 0..<2 {
                let y = (Double(ring) - 0.5) * region.height
                for index in 0..<segments {
                    let angle = Double(index) / Double(segments) * 2 * .pi
                    vertices.append(SCNVector3(cos(angle) * region.radius, y, sin(angle) * region.radius))
                    edges += [ring * segments + index, ring * segments + (index + 1) % segments]
                }
            }
            for index in stride(from: UInt32(0), to: segments, by: 16) {
                edges += [index, segments + index]
            }
        }
        return SCNGeometry(
            sources: [SCNGeometrySource(vertices: vertices)],
            elements: [SCNGeometryElement(indices: edges, primitiveType: .line)])
    }
}
