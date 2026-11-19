import SceneKit
import simd

struct PhotographedView {
    let offset: SCNVector3
    let up: SCNVector3

    init(bounds: SceneBounds, cameras: [CameraPose]) {
        let positions = cameras.map(\.centre).filter { $0.count == 3 && $0.allSatisfy(\.isFinite) }
        guard !positions.isEmpty else {
            offset = SceneViewDirection.north.offset
            up = SceneViewDirection.north.up
            return
        }
        let median = (0..<3).map { axis in positions.map { $0[axis] }.sorted()[positions.count / 2] }
        let direction = SIMD3<Double>(
            median[0] - bounds.centre.x, median[1] - bounds.centre.y, median[2] - bounds.centre.z)
        let length = simd_length(direction)
        guard length.isFinite, length > bounds.span * 0.001 else {
            offset = SceneViewDirection.north.offset
            up = SceneViewDirection.north.up
            return
        }
        let normal = direction / length
        offset = SCNVector3(normal.x, normal.y, normal.z)
        up = abs(normal.y) > 0.99 ? SCNVector3(0, 0, -1) : SCNVector3(0, 1, 0)
    }
}
