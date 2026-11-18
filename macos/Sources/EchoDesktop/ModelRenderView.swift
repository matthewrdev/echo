import SceneKit

enum ModelRenderView: String, Codable, CaseIterable {
    case front, back, left, right, top, oblique

    var offset: SCNVector3 {
        switch self {
        case .front: return SCNVector3(0, 0, 1)
        case .back: return SCNVector3(0, 0, -1)
        case .left: return SCNVector3(-1, 0, 0)
        case .right: return SCNVector3(1, 0, 0)
        case .top: return SCNVector3(0, 1, 0)
        case .oblique:
            let length = sqrt(0.3 * 0.3 + 0.18 * 0.18 + 0.96 * 0.96)
            return SCNVector3(0.3 / length, 0.18 / length, 0.96 / length)
        }
    }

    var up: SCNVector3 {
        self == .top ? SCNVector3(0, 0, -1) : SCNVector3(0, 1, 0)
    }
}
