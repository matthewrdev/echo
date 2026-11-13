import SceneKit

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

    func fit(camera: SCNNode, size: CGSize) {
        let aspect = max(0.1, Double(size.width / max(1, size.height)))
        let tangent = tan(25 * Double.pi / 180)
        let halfWidth = max(0.005, (maximum.x - minimum.x) / 2)
        let halfHeight = max(0.005, (maximum.y - minimum.y) / 2)
        let distance =
            max(halfHeight / tangent, halfWidth / (tangent * aspect)) * 1.08
            + (maximum.z - minimum.z) / 2
        camera.camera?.fieldOfView = 50
        camera.camera?.projectionDirection = .vertical
        camera.camera?.zNear = max(0.0001, span / 100_000)
        camera.camera?.zFar = max(1000, span * 100)
        camera.position = SCNVector3(centre.x, centre.y, centre.z + distance)
        camera.look(at: centre)
    }
}
