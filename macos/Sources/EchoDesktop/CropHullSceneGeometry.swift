import SceneKit

struct CropHullSceneGeometry {
    func make(_ hull: CropHull, outline: Bool) -> SCNGeometry {
        let vertices = hull.vertices.map { SCNVector3($0[0], $0[1], $0[2]) }
        let count = UInt32(vertices.count / 2)
        var indices: [UInt32] = []
        for index in 0..<count {
            let next = (index + 1) % count
            if outline {
                indices += [index, next, index + count, next + count, index, index + count]
            } else {
                indices += [index, next, next + count, index, next + count, index + count]
            }
        }
        if !outline {
            for index in 1..<(count - 1) {
                indices += [0, index, index + 1, count, count + index + 1, count + index]
            }
        }
        return SCNGeometry(
            sources: [SCNGeometrySource(vertices: vertices)],
            elements: [SCNGeometryElement(indices: indices, primitiveType: outline ? .line : .triangles)])
    }
}
