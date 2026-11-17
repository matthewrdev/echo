import Foundation
import simd

struct TexturePriorityPreview: Sendable {
    var positions: [SIMD3<Float>] = []
    var colours: [SIMD4<Float>] = []

    init(source: CropPointSource, strokes: [TexturePriorityStroke]) throws {
        let field = TexturePriorityField(strokes: strokes)
        var influences: [Int: Double] = [:]
        try source.data.withUnsafeBytes { bytes in
            for index in 0..<source.count {
                if index % 4096 == 0 { try Task.checkCancellation() }
                let point = source.position(index, bytes: bytes)
                let weight = field.logWeight(at: point, influences: &influences)
                guard abs(weight) > 0.02 else { continue }
                positions.append(SIMD3(Float(point.x), Float(point.y), Float(point.z)))
                let strength = Float(min(1, abs(weight) / 2))
                colours.append(
                    weight > 0
                        ? SIMD4(1, 0.85 - 0.55 * strength, 0.1, 0.85)
                        : SIMD4(0.1, 0.8 - 0.45 * strength, 1, 0.85))
            }
        }
    }
}
