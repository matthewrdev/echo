import SceneKit

package enum SceneViewDirection {
    case north, east, top

    package var offset: SCNVector3 {
        switch self {
        case .north: return SCNVector3(0, 0, 1)
        case .east: return SCNVector3(-1, 0, 0)
        case .top: return SCNVector3(0, 1, 0)
        }
    }

    package var up: SCNVector3 {
        self == .top ? SCNVector3(0, 0, -1) : SCNVector3(0, 1, 0)
    }
}
