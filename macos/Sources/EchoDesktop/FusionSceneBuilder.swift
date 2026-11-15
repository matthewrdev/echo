import SceneKit
import simd

struct FusionSceneBuilder {
    func geometry(surface: FusionSurface, showSupport: Bool) -> SCNGeometry {
        let positions = source(surface.positions, semantic: .vertex)
        let normals = source(surface.normals, semantic: .normal)
        var sources = [positions, normals]
        if showSupport {
            let colors = surface.support.map { FusionPalette().support($0) }
            sources.append(
                SCNGeometrySource(
                    data: colors.withUnsafeBufferPointer { Data(buffer: $0) }, semantic: .color,
                    vectorCount: colors.count, usesFloatComponents: true, componentsPerVector: 4,
                    bytesPerComponent: 4, dataOffset: 0, dataStride: MemoryLayout<SIMD4<Float>>.stride))
        }
        let geometry = SCNGeometry(
            sources: sources, elements: [SCNGeometryElement(indices: surface.indices, primitiveType: .triangles)])
        let material = SCNMaterial()
        material.lightingModel = showSupport ? .constant : .lambert
        material.diffuse.contents =
            showSupport ? NSColor.white : NSColor(calibratedRed: 0.64, green: 0.77, blue: 0.80, alpha: 1)
        material.isDoubleSided = true
        geometry.materials = [material]
        return geometry
    }

    func light(_ scene: SCNScene) {
        let ambient = SCNNode()
        ambient.light = SCNLight()
        ambient.light?.type = .ambient
        ambient.light?.intensity = 250
        scene.rootNode.addChildNode(ambient)
        let key = SCNNode()
        key.light = SCNLight()
        key.light?.type = .directional
        key.light?.intensity = 650
        key.eulerAngles = SCNVector3(-0.5, -0.6, 0)
        scene.rootNode.childNode(withName: "viewer-camera", recursively: false)?.addChildNode(key)
    }

    private func source(_ values: [SIMD3<Float>], semantic: SCNGeometrySource.Semantic) -> SCNGeometrySource {
        SCNGeometrySource(
            data: values.withUnsafeBufferPointer { Data(buffer: $0) }, semantic: semantic,
            vectorCount: values.count, usesFloatComponents: true, componentsPerVector: 3,
            bytesPerComponent: 4, dataOffset: 0, dataStride: MemoryLayout<SIMD3<Float>>.stride)
    }
}
