import AppKit
import SceneKit
import simd

package struct SurfaceGeometryBuilder {
    package init() {}

    package func make(surface: Surface, uvs: [[Double]], texture: NSImage?, mode: MeshViewMode) -> SCNGeometry {
        if mode == .normals { return normalGeometry(surface) }

        let vertices = surface.vertices.map { SCNVector3($0[0], $0[1], $0[2]) }
        var sources = [SCNGeometrySource(vertices: vertices)]
        if uvs.count == vertices.count {
            // Saved atlas and GLB coordinates start at the top-left, as do SceneKit image textures.
            sources.append(SCNGeometrySource(textureCoordinates: uvs.map { CGPoint(x: $0[0], y: $0[1]) }))
        }
        let indices = surface.faces.flatMap { $0.map(UInt32.init) }
        let element = SCNGeometryElement(indices: indices, primitiveType: .triangles)
        let geometry = SCNGeometry(sources: sources, elements: [element])
        let material = SCNMaterial()
        material.diffuse.contents =
            mode == .textured
            ? texture ?? NSColor(calibratedWhite: 0.72, alpha: 1)
            : mode == .wireframe ? NSColor.systemTeal : NSColor(calibratedWhite: 0.72, alpha: 1)
        material.diffuse.magnificationFilter = .linear
        material.diffuse.minificationFilter = .linear
        material.diffuse.mipFilter = .linear
        material.isDoubleSided = true
        material.fillMode = mode == .wireframe ? .lines : .fill
        material.lightingModel = .constant
        geometry.materials = [material]
        return geometry
    }

    private func normalGeometry(_ surface: Surface) -> SCNGeometry {
        var vertices: [SCNVector3] = []
        var colors: [SIMD4<Float>] = []
        vertices.reserveCapacity(surface.faces.count * 3)
        colors.reserveCapacity(surface.faces.count * 3)
        for face in surface.faces {
            let a = position(surface.vertices[face[0]])
            let b = position(surface.vertices[face[1]])
            let c = position(surface.vertices[face[2]])
            let cross = simd_cross(b - a, c - a)
            let length = simd_length(cross)
            // Keep each face separate so averaging cannot hide reversed winding or sharp edges.
            // A degenerate triangle has no direction and receives neutral grey.
            let normal = length.isFinite && length > 0 ? cross / length : SIMD3<Double>.zero
            let color = SIMD4<Float>(
                Float(normal.x * 0.5 + 0.5), Float(normal.y * 0.5 + 0.5), Float(normal.z * 0.5 + 0.5), 1)
            for vertex in [a, b, c] {
                vertices.append(SCNVector3(vertex.x, vertex.y, vertex.z))
                colors.append(color)
            }
        }
        let colorSource = SCNGeometrySource(
            data: colors.withUnsafeBufferPointer { Data(buffer: $0) },
            semantic: .color, vectorCount: colors.count, usesFloatComponents: true,
            componentsPerVector: 4, bytesPerComponent: MemoryLayout<Float>.size,
            dataOffset: 0, dataStride: MemoryLayout<SIMD4<Float>>.stride)
        let element = SCNGeometryElement(indices: Array(0..<UInt32(vertices.count)), primitiveType: .triangles)
        let geometry = SCNGeometry(sources: [SCNGeometrySource(vertices: vertices), colorSource], elements: [element])
        let material = SCNMaterial()
        material.diffuse.contents = NSColor.white
        material.isDoubleSided = true
        material.lightingModel = .constant
        geometry.materials = [material]
        return geometry
    }

    private func position(_ values: [Double]) -> SIMD3<Double> {
        SIMD3(values[0], values[1], values[2])
    }
}
