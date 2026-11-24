import Foundation

package struct PackedGeometryReader {
    package init() {}

    package func surface(vertices: BinaryRecordBuffer, faces: BinaryRecordBuffer, images: [String]) throws -> Surface {
        var points: [[Double]] = []
        points.reserveCapacity(vertices.count)
        for index in 0..<vertices.count {
            if index % 4096 == 0 { try Task.checkCancellation() }
            points.append([vertices.double(index), vertices.double(index, 8), vertices.double(index, 16)])
        }

        var triangles: [[Int]] = []
        triangles.reserveCapacity(faces.count)
        for index in 0..<faces.count {
            if index % 4096 == 0 { try Task.checkCancellation() }
            let face = [faces.integer(index), faces.integer(index, 4), faces.integer(index, 8)]
            guard face.allSatisfy(points.indices.contains) else {
                throw ModelRenderError(message: "Invalid binary triangle index")
            }
            triangles.append(face)
        }
        return Surface(vertices: points, faces: triangles, sourceImages: images)
    }

    package func coordinates(_ records: BinaryRecordBuffer) throws -> [[Double]] {
        var values: [[Double]] = []
        values.reserveCapacity(records.count)
        for index in 0..<records.count {
            if index % 4096 == 0 { try Task.checkCancellation() }
            values.append([records.double(index), records.double(index, 8)])
        }
        return values
    }
}
