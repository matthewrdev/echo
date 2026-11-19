import SceneKit
import simd

@MainActor
struct ProjectedCropHullBuilder {
    func make(points: [CGPoint], in view: SCNView, mode: CropRegionMode) throws -> ExclusionRegion {
        SCNTransaction.flush()
        guard points.count >= 3, let camera = view.pointOfView?.presentation,
            let lens = camera.camera,
            let artifact = view.scene?.rootNode.childNode(withName: "artifact", recursively: false)
        else { throw CLIError(message: "Load a model or point cloud before painting a crop.") }
        let box = artifact.boundingBox
        var depths: [Double] = []
        for x in [box.min.x, box.max.x] {
            for y in [box.min.y, box.max.y] {
                for z in [box.min.z, box.max.z] {
                    let world = artifact.convertPosition(SCNVector3(x, y, z), to: nil)
                    depths.append(-camera.convertPosition(world, from: nil).z)
                }
            }
        }
        let span = max(0.001, (depths.max() ?? 1) - (depths.min() ?? 0))
        let near = max((camera.camera?.zNear ?? 0.01) * 1.01, (depths.min() ?? 0) - span * 0.05)
        let far = max(near + span, (depths.max() ?? 1) + span * 0.05)
        let cameraPosition = camera.worldPosition
        let eye = SIMD3<Double>(cameraPosition.x, cameraPosition.y, cameraPosition.z)
        let cameraForward = camera.worldFront
        let forward = simd_normalize(SIMD3<Double>(cameraForward.x, cameraForward.y, cameraForward.z))
        let projection = simd_float4x4(lens.projectionTransform(withViewportSize: view.bounds.size))
        let inverseProjection = simd_double4x4(
            SIMD4<Double>(projection.columns.0), SIMD4<Double>(projection.columns.1),
            SIMD4<Double>(projection.columns.2), SIMD4<Double>(projection.columns.3)
        ).inverse
        let transform = camera.simdWorldTransform
        let worldTransform = simd_double4x4(
            SIMD4<Double>(transform.columns.0), SIMD4<Double>(transform.columns.1),
            SIMD4<Double>(transform.columns.2), SIMD4<Double>(transform.columns.3))
        var vertices: [SIMD3<Double>] = []
        for distance in [near, far] {
            for point in points {
                let y = view.isFlipped ? view.bounds.height - point.y : point.y
                // Keep the ray in camera space until after projection. SceneKit's
                // float world-space unprojection loses a tiny near-plane offset
                // when the eye is hundreds of metres from the scene origin.
                let clip = SIMD4<Double>(
                    2 * point.x / view.bounds.width - 1, 2 * y / view.bounds.height - 1, 0, 1)
                let local = inverseProjection * clip
                let localPoint = local / local.w
                let world = worldTransform * localPoint
                let start = SIMD3<Double>(world.x, world.y, world.z)
                let worldRay = worldTransform * SIMD4<Double>(localPoint.x, localPoint.y, localPoint.z, 0)
                let orthographic = lens.usesOrthographicProjection
                let origin = orthographic ? start : eye
                let direction = orthographic ? forward : simd_normalize(SIMD3(worldRay.x, worldRay.y, worldRay.z))
                let denominator = simd_dot(direction, forward)
                guard denominator > 0.000001 else { throw CLIError(message: "The crop must face the model.") }
                // Intersect rays with camera-depth planes in double precision. Directly
                // unprojecting far depth-buffer values loses precision on large scenes.
                let along = (distance - simd_dot(origin - eye, forward)) / denominator
                vertices.append(origin + direction * along)
            }
        }
        let centre = vertices.reduce(SIMD3<Double>.zero, +) / Double(vertices.count)
        func plane(_ a: Int, _ b: Int, _ c: Int) throws -> CropPlane {
            let cross = simd_cross(vertices[b] - vertices[a], vertices[c] - vertices[a])
            guard simd_length(cross) > 1e-12 else { throw CLIError(message: "Paint a larger crop region.") }
            var normal = simd_normalize(cross)
            if simd_dot(normal, centre - vertices[a]) > 0 { normal = -normal }
            return CropPlane(
                normalX: normal.x, normalY: normal.y, normalZ: normal.z, offset: simd_dot(normal, vertices[a]))
        }
        let count = points.count
        var planes = [
            CropPlane(
                normalX: -forward.x, normalY: -forward.y, normalZ: -forward.z,
                offset: -simd_dot(forward, eye) - near),
            CropPlane(
                normalX: forward.x, normalY: forward.y, normalZ: forward.z,
                offset: simd_dot(forward, eye) + far),
        ]
        for index in 0..<count {
            planes.append(try plane(index + count, (index + 1) % count + count, index))
        }
        let hull = CropHull(
            vertices: vertices.map { [$0.x, $0.y, $0.z] }, planes: planes,
            tolerance: max(0.000001, span * 0.00001))
        guard hull.isValid else {
            throw CLIError(message: "The painted crop could not be projected. Reset the view and try again.")
        }
        var region = ExclusionRegion(shape: .projectedHull)
        region.mode = mode
        region.hull = hull
        return region
    }
}
