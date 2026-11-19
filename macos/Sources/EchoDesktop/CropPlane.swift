import simd

struct CropPlane: Codable, Hashable, Sendable {
    var normalX: Double
    var normalY: Double
    var normalZ: Double
    var offset: Double

    var normal: SIMD3<Double> { SIMD3(normalX, normalY, normalZ) }
    func distance(_ point: SIMD3<Double>) -> Double { simd_dot(normal, point) - offset }
}
