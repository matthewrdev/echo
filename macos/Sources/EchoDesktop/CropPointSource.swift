import SceneKit
import simd

// Copy only the immutable buffer metadata on the main actor. Counting reads the
// existing Data on a background task without touching the live SceneKit graph.
struct CropPointSource: Sendable {
    let data: Data
    let count: Int
    let offset: Int
    let stride: Int
    let componentWidth: Int
    let transform: simd_double4x4
    let isMesh: Bool

    @MainActor
    init?(node: SCNNode) {
        guard let geometry = node.geometry,
            geometry.elements.contains(where: { $0.primitiveType == .point || $0.primitiveType == .triangles }),
            let source = geometry.sources(for: .vertex).first,
            source.usesFloatComponents, source.componentsPerVector >= 3,
            source.bytesPerComponent == 4 || source.bytesPerComponent == 8,
            source.vectorCount > 0
        else { return nil }
        data = source.data
        count = source.vectorCount
        offset = source.dataOffset
        stride = source.dataStride
        componentWidth = source.bytesPerComponent
        guard offset >= 0, stride >= componentWidth * 3,
            offset + (count - 1) * stride + componentWidth * 3 <= data.count
        else { return nil }
        let matrix = node.worldTransform
        transform = simd_double4x4(
            columns: (
                SIMD4(matrix.m11, matrix.m12, matrix.m13, matrix.m14),
                SIMD4(matrix.m21, matrix.m22, matrix.m23, matrix.m24),
                SIMD4(matrix.m31, matrix.m32, matrix.m33, matrix.m34),
                SIMD4(matrix.m41, matrix.m42, matrix.m43, matrix.m44)
            ))
        isMesh = geometry.elements.contains { $0.primitiveType == .triangles }
    }

    func position(_ index: Int, bytes: UnsafeRawBufferPointer) -> SIMD3<Double> {
        let start = offset + index * stride
        let local = SIMD4(
            component(bytes, at: start), component(bytes, at: start + componentWidth),
            component(bytes, at: start + componentWidth * 2), 1)
        let world = transform * local
        return SIMD3(world.x, world.y, world.z)
    }

    private func component(_ bytes: UnsafeRawBufferPointer, at offset: Int) -> Double {
        componentWidth == 4
            ? Double(bytes.loadUnaligned(fromByteOffset: offset, as: Float.self))
            : bytes.loadUnaligned(fromByteOffset: offset, as: Double.self)
    }
}
