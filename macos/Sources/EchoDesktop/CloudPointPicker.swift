import SceneKit
import simd

struct CloudPointPicker {
    func pick(at location: NSPoint, in view: SCNView, node: SCNNode) -> SCNVector3? {
        guard let source = node.geometry?.sources(for: .vertex).first,
            source.usesFloatComponents, source.componentsPerVector >= 3,
            source.bytesPerComponent == 4 || source.bytesPerComponent == 8
        else { return nil }
        let y = view.isFlipped ? view.bounds.height - location.y : location.y
        let near = vector(view.unprojectPoint(SCNVector3(location.x, y, 0)))
        let far = vector(view.unprojectPoint(SCNVector3(location.x, y, 1)))
        let rayLength = simd_length(far - near)
        guard rayLength.isFinite, rayLength > 0 else { return nil }
        let direction = (far - near) / rayLength
        let nearRadius = simd_length(vector(view.unprojectPoint(SCNVector3(location.x + 12, y, 0))) - near)
        let farRadius = simd_length(vector(view.unprojectPoint(SCNVector3(location.x + 12, y, 1))) - far)
        let transform = node.presentation.worldTransform
        var best: SCNVector3?
        var bestDistance = 12.0
        var bestDepth = Double.infinity
        source.data.withUnsafeBytes { bytes in
            for index in 0..<source.vectorCount {
                let offset = source.dataOffset + index * source.dataStride
                let x = component(bytes, at: offset, width: source.bytesPerComponent)
                let y = component(bytes, at: offset + source.bytesPerComponent, width: source.bytesPerComponent)
                let z = component(bytes, at: offset + source.bytesPerComponent * 2, width: source.bytesPerComponent)
                let world = SIMD3<Double>(
                    transform.m11 * x + transform.m21 * y + transform.m31 * z + transform.m41,
                    transform.m12 * x + transform.m22 * y + transform.m32 * z + transform.m42,
                    transform.m13 * x + transform.m23 * y + transform.m33 * z + transform.m43)
                let delta = world - near
                let along = simd_dot(delta, direction)
                guard along >= 0, along <= rayLength else { continue }
                // Reject points outside the click ray before asking SceneKit to
                // project them; saved cloud previews can contain millions of points.
                let radius = nearRadius + (farRadius - nearRadius) * along / rayLength
                guard simd_length_squared(delta - direction * along) <= radius * radius * 4 else { continue }
                let point = SCNVector3(world.x, world.y, world.z)
                let projected = view.projectPoint(point)
                guard projected.z >= 0, projected.z <= 1 else { continue }
                let projectedY = view.isFlipped ? view.bounds.height - projected.y : projected.y
                let distance = hypot(projected.x - location.x, projectedY - location.y)
                let closer = distance < bestDistance - 0.1
                let samePixel = abs(distance - bestDistance) <= 0.1 && projected.z < bestDepth
                if closer || samePixel {
                    best = point
                    bestDistance = distance
                    bestDepth = projected.z
                }
            }
        }
        return best
    }

    private func vector(_ point: SCNVector3) -> SIMD3<Double> {
        SIMD3(point.x, point.y, point.z)
    }

    private func component(_ bytes: UnsafeRawBufferPointer, at offset: Int, width: Int) -> Double {
        width == 4
            ? Double(bytes.loadUnaligned(fromByteOffset: offset, as: Float.self))
            : bytes.loadUnaligned(fromByteOffset: offset, as: Double.self)
    }
}
