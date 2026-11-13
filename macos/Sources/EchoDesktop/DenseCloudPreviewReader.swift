import SceneKit

final class DenseCloudPreviewReader {
    func read(file: URL, expectedCount: Int, limit: Int, minimumSupport: Double) throws -> DenseCloudPreview {
        let bytes = try Data(contentsOf: file, options: .alwaysMapped)
        let prefix = bytes.prefix(8192)
        guard let end = prefix.range(of: Data("end_header\n".utf8)) else {
            throw CLIError(message: "Dense cloud has no supported PLY header.")
        }
        let header = String(decoding: prefix[..<end.upperBound], as: UTF8.self)
        let expectedHeader =
            "ply\nformat binary_little_endian 1.0\nelement vertex \(expectedCount)\nproperty float x\nproperty float y\nproperty float z\nproperty uchar red\nproperty uchar green\nproperty uchar blue\nproperty int view\nproperty float confidence\nend_header\n"
        guard expectedCount >= 0, header == expectedHeader,
            (bytes.count - end.upperBound) / 23 == expectedCount,
            (bytes.count - end.upperBound) % 23 == 0
        else { throw CLIError(message: "Dense cloud layout or point count does not match its checkpoint.") }
        let budget = min(expectedCount, max(1, limit))
        var packed = Data(count: budget * 16)
        var retained = 0
        try bytes.withUnsafeBytes { source in
            try packed.withUnsafeMutableBytes { destination in
                for sample in 0..<budget {
                    if sample.isMultiple(of: 4096) { try Task.checkCancellation() }
                    // One midpoint from every equal interval spans the entire saved cloud
                    // without a large point-object array or a copy of the mapped PLY.
                    let index = Int((Int64(sample) * 2 + 1) * Int64(expectedCount) / (Int64(budget) * 2))
                    let input = end.upperBound + index * 23
                    let confidence = Float(
                        bitPattern: UInt32(
                            littleEndian: source.loadUnaligned(fromByteOffset: input + 19, as: UInt32.self)))
                    guard confidence.isFinite, Double(confidence) >= minimumSupport else { continue }
                    let x = Float(
                        bitPattern: UInt32(littleEndian: source.loadUnaligned(fromByteOffset: input, as: UInt32.self)))
                    let y = Float(
                        bitPattern: UInt32(
                            littleEndian: source.loadUnaligned(fromByteOffset: input + 4, as: UInt32.self)))
                    let z = Float(
                        bitPattern: UInt32(
                            littleEndian: source.loadUnaligned(fromByteOffset: input + 8, as: UInt32.self)))
                    guard x.isFinite, y.isFinite, z.isFinite else { continue }
                    let output = retained * 16
                    destination.storeBytes(of: x, toByteOffset: output, as: Float.self)
                    destination.storeBytes(of: y, toByteOffset: output + 4, as: Float.self)
                    destination.storeBytes(of: z, toByteOffset: output + 8, as: Float.self)
                    destination[output + 12] = source[input + 12]
                    destination[output + 13] = source[input + 13]
                    destination[output + 14] = source[input + 14]
                    destination[output + 15] = 255
                    retained += 1
                }
            }
        }
        packed.count = retained * 16
        try Task.checkCancellation()
        let positions = SCNGeometrySource(
            data: packed, semantic: .vertex, vectorCount: retained,
            usesFloatComponents: true, componentsPerVector: 3, bytesPerComponent: 4, dataOffset: 0, dataStride: 16)
        let colors = SCNGeometrySource(
            data: packed, semantic: .color, vectorCount: retained,
            usesFloatComponents: false, componentsPerVector: 4, bytesPerComponent: 1, dataOffset: 12, dataStride: 16)
        let element = SCNGeometryElement(data: nil, primitiveType: .point, primitiveCount: retained, bytesPerIndex: 4)
        element.pointSize = 2
        element.minimumPointScreenSpaceRadius = 0.6
        element.maximumPointScreenSpaceRadius = 3
        let geometry = SCNGeometry(sources: [positions, colors], elements: [element])
        // SceneKit exposes integer color attributes as 0...255 values. Normalize
        // in the vertex shader so the CPU/GPU buffer stays at 16 bytes per point.
        geometry.shaderModifiers = [.geometry: "_geometry.color /= 255.0;"]
        geometry.firstMaterial?.lightingModel = .constant
        return DenseCloudPreview(geometry: geometry, count: retained)
    }
}
