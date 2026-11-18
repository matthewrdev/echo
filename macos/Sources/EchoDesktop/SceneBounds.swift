import SceneKit
import simd

struct SceneBounds {
    let minimum: SCNVector3
    let maximum: SCNVector3

    init(positions: [[Double]]) {
        var low = SCNVector3(Double.infinity, Double.infinity, Double.infinity)
        var high = SCNVector3(-Double.infinity, -Double.infinity, -Double.infinity)
        for p in positions where p.count == 3 && p.allSatisfy(\.isFinite) {
            low.x = min(low.x, p[0])
            low.y = min(low.y, p[1])
            low.z = min(low.z, p[2])
            high.x = max(high.x, p[0])
            high.y = max(high.y, p[1])
            high.z = max(high.z, p[2])
        }
        minimum = low.x.isFinite ? low : SCNVector3(-1, -1, -1)
        maximum = high.x.isFinite ? high : SCNVector3(1, 1, 1)
    }

    var centre: SCNVector3 {
        SCNVector3((minimum.x + maximum.x) / 2, (minimum.y + maximum.y) / 2, (minimum.z + maximum.z) / 2)
    }

    var span: Double {
        max(0.01, max(maximum.x - minimum.x, max(maximum.y - minimum.y, maximum.z - minimum.z)))
    }

    func fit(camera: SCNNode, size: CGSize, direction: SceneViewDirection = .north) {
        fit(camera: camera, size: size, offset: direction.offset, up: direction.up)
    }

    func fit(camera: SCNNode, size: CGSize, offset: SCNVector3, up: SCNVector3) {
        let aspect = max(0.1, Double(size.width / max(1, size.height)))
        let tangent = tan(25 * Double.pi / 180)
        camera.position = SCNVector3(centre.x + offset.x, centre.y + offset.y, centre.z + offset.z)
        // An explicit camera basis also handles the exact 180-degree back view,
        // where SceneKit's look-at rotation can leave an identity orientation.
        let backward = simd_normalize(SIMD3<Float>(Float(offset.x), Float(offset.y), Float(offset.z)))
        let horizontal = simd_normalize(simd_cross(SIMD3<Float>(Float(up.x), Float(up.y), Float(up.z)), backward))
        let vertical = simd_cross(backward, horizontal)
        camera.simdOrientation = simd_quatf(simd_float3x3(columns: (horizontal, vertical, backward)))
        var distance = 0.01
        // Fit every corner in the chosen camera basis, including depth and aspect.
        for x in [minimum.x, maximum.x] {
            for y in [minimum.y, maximum.y] {
                for z in [minimum.z, maximum.z] {
                    let local = camera.convertVector(SCNVector3(x - centre.x, y - centre.y, z - centre.z), from: nil)
                    distance = max(
                        distance, local.z + max(abs(local.y) / tangent, abs(local.x) / (tangent * aspect)) * 1.08)
                }
            }
        }
        camera.camera?.usesOrthographicProjection = false
        camera.camera?.fieldOfView = 50
        camera.camera?.projectionDirection = .vertical
        camera.camera?.zNear = max(0.0001, span / 100_000)
        camera.camera?.zFar = max(1000, span * 100)
        camera.position = SCNVector3(
            centre.x + offset.x * distance, centre.y + offset.y * distance, centre.z + offset.z * distance)
    }
}
