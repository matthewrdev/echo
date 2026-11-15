import Foundation
import simd

// A display-only zero surface. Missing samples stay unknown: no hole repair or simplification.
struct FusionSurfaceBuilder {
    private let corners = [
        FusionGridPoint(x: 0, y: 0, z: 0), FusionGridPoint(x: 1, y: 0, z: 0),
        FusionGridPoint(x: 1, y: 1, z: 0), FusionGridPoint(x: 0, y: 1, z: 0),
        FusionGridPoint(x: 0, y: 0, z: 1), FusionGridPoint(x: 1, y: 0, z: 1),
        FusionGridPoint(x: 1, y: 1, z: 1), FusionGridPoint(x: 0, y: 1, z: 1),
    ]
    private let edges = [
        [0, 1], [1, 2], [2, 3], [3, 0], [4, 5], [5, 6], [6, 7], [7, 4], [0, 4], [1, 5], [2, 6], [3, 7],
    ]
    private let quads = [
        [
            FusionGridPoint(x: 0, y: 0, z: 0), FusionGridPoint(x: 0, y: -1, z: 0), FusionGridPoint(x: 0, y: -1, z: -1),
            FusionGridPoint(x: 0, y: 0, z: -1),
        ],
        [
            FusionGridPoint(x: 0, y: 0, z: 0), FusionGridPoint(x: 0, y: 0, z: -1), FusionGridPoint(x: -1, y: 0, z: -1),
            FusionGridPoint(x: -1, y: 0, z: 0),
        ],
        [
            FusionGridPoint(x: 0, y: 0, z: 0), FusionGridPoint(x: -1, y: 0, z: 0), FusionGridPoint(x: -1, y: -1, z: 0),
            FusionGridPoint(x: 0, y: -1, z: 0),
        ],
    ]
    private let directions = [
        FusionGridPoint(x: 1, y: 0, z: 0), FusionGridPoint(x: 0, y: 1, z: 0), FusionGridPoint(x: 0, y: 0, z: 1),
    ]

    func build(_ volume: FusedVolume) throws -> FusionSurface {
        _ = try FusionVolumeBounds(volume: volume)
        var lookup: [FusionGridPoint: Int] = [:]
        lookup.reserveCapacity(volume.samples.count)
        for (index, sample) in volume.samples.enumerated() {
            if index % 4096 == 0 { try Task.checkCancellation() }
            let key = FusionGridPoint(x: sample.x, y: sample.y, z: sample.z)
            guard lookup.updateValue(index, forKey: key) == nil else {
                throw CLIError(message: "The saved fusion volume contains duplicate grid samples.")
            }
        }
        var result = FusionSurface()
        var cells: [FusionGridPoint: UInt32] = [:]
        var values = [Float](repeating: 0, count: 8)
        for (index, sample) in volume.samples.enumerated() {
            if index % 1024 == 0 { try Task.checkCancellation() }
            let key = FusionGridPoint(x: sample.x, y: sample.y, z: sample.z)
            var support = Int.max
            var complete = true
            for corner in corners.indices {
                guard let source = lookup[key.adding(corners[corner])] else {
                    complete = false
                    break
                }
                values[corner] = volume.samples[source].distance
                support = min(support, volume.samples[source].views)
            }
            guard complete else { continue }
            let crossesSurface = values.contains(where: { $0 < 0 }) && values.contains(where: { $0 >= 0 })
            guard crossesSurface else { continue }
            var position = SIMD3<Double>(repeating: 0)
            var crossings = 0
            for edge in edges {
                let a = edge[0]
                let b = edge[1]
                guard (values[a] < 0) != (values[b] < 0) else { continue }
                let fraction = Double(values[a]) / (Double(values[a]) - Double(values[b]))
                let start = SIMD3<Double>(Double(corners[a].x), Double(corners[a].y), Double(corners[a].z))
                let end = SIMD3<Double>(Double(corners[b].x), Double(corners[b].y), Double(corners[b].z))
                position += start + (end - start) * fraction
                crossings += 1
            }
            position =
                (SIMD3<Double>(Double(key.x), Double(key.y), Double(key.z)) + position / Double(crossings))
                * volume.voxelSize
            cells[key] = UInt32(result.positions.count)
            let vertex = SIMD3<Float>(position)
            guard vertex.x.isFinite && vertex.y.isFinite && vertex.z.isFinite else {
                throw CLIError(message: "The fusion coordinates exceed the viewer’s supported range.")
            }
            result.positions.append(vertex)
            result.support.append(support)
        }
        var indices = [UInt32](repeating: 0, count: 4)
        for (index, sample) in volume.samples.enumerated() {
            if index % 1024 == 0 { try Task.checkCancellation() }
            let key = FusionGridPoint(x: sample.x, y: sample.y, z: sample.z)
            for axis in 0..<3 {
                guard let next = lookup[key.adding(directions[axis])],
                    (sample.distance < 0) != (volume.samples[next].distance < 0)
                else { continue }
                var complete = true
                for corner in 0..<4 {
                    guard let vertex = cells[key.adding(quads[axis][corner])] else {
                        complete = false
                        break
                    }
                    indices[corner] = vertex
                }
                guard complete else { continue }
                if sample.distance >= 0 { indices.reverse() }
                result.indices.append(contentsOf: [
                    indices[0], indices[1], indices[2], indices[0], indices[2], indices[3],
                ])
            }
        }
        result = try compact(result)
        result.normals = Array(repeating: SIMD3<Float>(repeating: 0), count: result.positions.count)
        for triangle in stride(from: 0, to: result.indices.count, by: 3) {
            if triangle % 12288 == 0 { try Task.checkCancellation() }
            let a = Int(result.indices[triangle])
            let b = Int(result.indices[triangle + 1])
            let c = Int(result.indices[triangle + 2])
            let normal = simd_cross(
                result.positions[b] - result.positions[a], result.positions[c] - result.positions[a])
            result.normals[a] += normal
            result.normals[b] += normal
            result.normals[c] += normal
        }
        for index in result.normals.indices {
            if index % 4096 == 0 { try Task.checkCancellation() }
            let normal = result.normals[index]
            result.normals[index] = simd_length_squared(normal) > 0 ? simd_normalize(normal) : SIMD3<Float>(0, 1, 0)
        }
        return result
    }

    private func compact(_ surface: FusionSurface) throws -> FusionSurface {
        // Unconnected crossings have no visible triangles and must not distort camera fitting.
        var result = FusionSurface()
        var mapping = [Int](repeating: -1, count: surface.positions.count)
        result.indices.reserveCapacity(surface.indices.count)
        for (index, vertex) in surface.indices.enumerated() {
            if index % 4096 == 0 { try Task.checkCancellation() }
            let source = Int(vertex)
            if mapping[source] < 0 {
                mapping[source] = result.positions.count
                result.positions.append(surface.positions[source])
                result.support.append(surface.support[source])
            }
            result.indices.append(UInt32(mapping[source]))
        }
        return result
    }
}
