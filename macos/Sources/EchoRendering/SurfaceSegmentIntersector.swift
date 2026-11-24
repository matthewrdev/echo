import simd

// Tests the saved geometry directly, independently of renderer preparation and
// visibility. Both triangle windings count, including the back of a closed hull.
package struct SurfaceSegmentIntersector {
    package func intersections(surface: Surface, from start: SIMD3<Double>, to end: SIMD3<Double>) -> [SIMD3<Double>] {
        let direction = end - start
        let length = simd_length(direction)
        guard length > 0 else { return [] }
        var distances: [Double] = []
        for face in surface.faces {
            let a = vertex(surface.vertices[face[0]])
            let ab = vertex(surface.vertices[face[1]]) - a
            let ac = vertex(surface.vertices[face[2]]) - a
            let p = simd_cross(direction, ac)
            let determinant = simd_dot(ab, p)
            let scale = simd_length(ab) * simd_length(p)
            guard abs(determinant) > scale * 1e-12 else { continue }
            let offset = start - a
            let u = simd_dot(offset, p) / determinant
            guard u >= -1e-9, u <= 1 + 1e-9 else { continue }
            let q = simd_cross(offset, ab)
            let v = simd_dot(direction, q) / determinant
            guard v >= -1e-9, u + v <= 1 + 1e-9 else { continue }
            let distance = simd_dot(ac, q) / determinant
            if distance >= 0, distance <= 1 { distances.append(distance) }
        }
        var unique: [Double] = []
        for distance in distances.sorted() {
            // Shared edges and texture seams must not count as extra surfaces.
            if let previous = unique.last, (distance - previous) * length < 0.001 { continue }
            unique.append(distance)
        }
        return unique.map { start + direction * $0 }
    }

    private func vertex(_ values: [Double]) -> SIMD3<Double> {
        SIMD3(values[0], values[1], values[2])
    }
}
