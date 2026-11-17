import SceneKit
import simd

@MainActor
struct CropHandleDrag {
    let original: ExclusionRegion
    let handle: CropHandle
    private let start: NSPoint
    private let planeStart: SIMD3<Double>?
    private let axisStart: Double?
    private let unitsPerPoint: Double
    private let right: SIMD3<Double>

    init(region: ExclusionRegion, handle: CropHandle, start: NSPoint, view: SCNView) {
        original = region
        self.handle = handle
        self.start = start
        planeStart = Self.horizontalPoint(at: start, height: region.centreY, view: view)
        axisStart = Self.axisDistance(
            at: start, anchor: handle.position, axis: region.orientation.act(handle.kind.axis), view: view)
        let projected = view.projectPoint(handle.point)
        let adjacent = view.unprojectPoint(SCNVector3(projected.x + 1, projected.y, projected.z))
        unitsPerPoint = max(1e-10, simd_length(SIMD3(adjacent.x, adjacent.y, adjacent.z) - handle.position))
        let cameraRight = view.pointOfView?.simdWorldRight ?? SIMD3<Float>(1, 0, 0)
        let horizontal = SIMD3<Double>(Double(cameraRight.x), 0, Double(cameraRight.z))
        right = simd_length(horizontal) > 0.01 ? simd_normalize(horizontal) : SIMD3(1, 0, 0)
    }

    func region(at location: NSPoint, view: SCNView) -> ExclusionRegion? {
        var region = original
        let dx = Double(location.x - start.x)
        let dy = Double(location.y - start.y) * (view.isFlipped ? -1 : 1)
        switch handle.kind {
        case .move:
            let delta: SIMD3<Double>
            if let planeStart {
                guard let point = Self.horizontalPoint(at: location, height: original.centreY, view: view) else {
                    return nil
                }
                delta = point - planeStart
            } else {
                // Front views see the X/Z plane edge-on: horizontal moves across it, vertical moves into it.
                let forward = SIMD3(right.z, 0, -right.x)
                delta = (right * dx + forward * dy) * unitsPerPoint
            }
            region.centreX += delta.x
            region.centreZ += delta.z
        case .rotate:
            guard original.shape == .box else { return nil }
            var angle = dx * 0.5
            if let planeStart {
                guard let point = Self.horizontalPoint(at: location, height: original.centreY, view: view) else {
                    return nil
                }
                let centre = SIMD3(original.centreX, original.centreY, original.centreZ)
                let a = planeStart - centre
                let b = point - centre
                guard simd_length(a) > 1e-10, simd_length(b) > 1e-10 else { return nil }
                angle = atan2(a.z * b.x - a.x * b.z, simd_dot(a, b)) * 180 / .pi
            }
            region.rotationY = (original.rotationY + angle).truncatingRemainder(dividingBy: 360)
            if region.rotationY > 180 { region.rotationY -= 360 }
            if region.rotationY < -180 { region.rotationY += 360 }
        case .width, .depth, .height, .radius:
            var distance = dy * unitsPerPoint
            if let axisStart {
                let axis = original.orientation.act(handle.kind.axis)
                guard let current = Self.axisDistance(at: location, anchor: handle.position, axis: axis, view: view)
                else { return nil }
                distance = current - axisStart
            }
            resize(&region, by: distance)
        }
        return region.isValid ? region : nil
    }

    private func resize(_ region: inout ExclusionRegion, by distance: Double) {
        func size(_ value: Double, factor: Double = 2) -> Double {
            max(max(1e-8, value * 0.0001), value + distance * factor)
        }
        switch handle.kind {
        case .width: region.width = size(original.width)
        case .depth: region.depth = size(original.depth)
        case .height: region.height = size(original.height)
        case .radius: region.radius = size(original.radius, factor: 1)
        default: break
        }
    }

    private static func horizontalPoint(at point: NSPoint, height: Double, view: SCNView) -> SIMD3<Double>? {
        let y = view.isFlipped ? view.bounds.height - point.y : point.y
        let near = view.unprojectPoint(SCNVector3(point.x, y, 0))
        let far = view.unprojectPoint(SCNVector3(point.x, y, 1))
        let origin = SIMD3<Double>(near.x, near.y, near.z)
        let direction = simd_normalize(SIMD3<Double>(far.x - near.x, far.y - near.y, far.z - near.z))
        guard abs(direction.y) > 0.1 else { return nil }
        let distance = (height - origin.y) / direction.y
        guard distance >= 0, distance.isFinite else { return nil }
        return origin + direction * distance
    }

    private static func axisDistance(at point: NSPoint, anchor: SIMD3<Double>, axis: SIMD3<Double>, view: SCNView)
        -> Double?
    {
        let y = view.isFlipped ? view.bounds.height - point.y : point.y
        let near = view.unprojectPoint(SCNVector3(point.x, y, 0))
        let far = view.unprojectPoint(SCNVector3(point.x, y, 1))
        let direction = simd_normalize(SIMD3<Double>(far.x - near.x, far.y - near.y, far.z - near.z))
        let offset = SIMD3<Double>(near.x, near.y, near.z) - anchor
        let parallel = simd_dot(axis, direction)
        let denominator = 1 - parallel * parallel
        guard denominator > 0.01 else { return nil }
        let distance = (simd_dot(axis, offset) - parallel * simd_dot(direction, offset)) / denominator
        return distance.isFinite ? distance : nil
    }
}
