import Foundation
import simd

struct TextureBrushSample: Codable, Equatable, Sendable {
    var centre: [Double]
    var normal: [Double]
    var radius: Double
    var depth: Double

    var isValid: Bool {
        let vectors = centre.count == 3 && normal.count == 3
        guard vectors, centre.allSatisfy(\.isFinite), normal.allSatisfy(\.isFinite) else { return false }
        let unitNormal = abs(normal.reduce(0) { $0 + $1 * $1 } - 1) < 0.001
        return unitNormal && radius.isFinite && depth.isFinite && radius > 0 && depth > 0 && depth <= radius
    }

    func influence(at point: SIMD3<Double>) -> Double {
        let delta = point - SIMD3(centre[0], centre[1], centre[2])
        let direction = SIMD3(normal[0], normal[1], normal[2])
        let along = simd_dot(delta, direction)
        let radial = delta - direction * along
        let distance = sqrt(simd_length_squared(radial) / (radius * radius) + along * along / (depth * depth))
        let feather = min(1, max(0, (1 - distance) * 2))
        return feather * feather * (3 - 2 * feather)
    }
}
