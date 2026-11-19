import simd

struct CropHull: Codable, Hashable, Sendable {
    var vertices: [[Double]]
    var planes: [CropPlane]
    var tolerance = 0.000001

    var isValid: Bool {
        let validVertices =
            (6...256).contains(vertices.count) && vertices.count.isMultiple(of: 2)
            && vertices.allSatisfy { $0.count == 3 && $0.allSatisfy(\.isFinite) }
        let validPlanes =
            planes.count == vertices.count / 2 + 2
            && planes.allSatisfy {
                $0.offset.isFinite && abs(simd_length_squared($0.normal) - 1) < 0.000001
            }
        guard validVertices, validPlanes, tolerance.isFinite, tolerance > 0 else { return false }
        return vertices.allSatisfy { contains(SIMD3($0[0], $0[1], $0[2])) }
    }

    func contains(_ point: SIMD3<Double>) -> Bool {
        planes.allSatisfy { $0.distance(point) <= tolerance }
    }
}
