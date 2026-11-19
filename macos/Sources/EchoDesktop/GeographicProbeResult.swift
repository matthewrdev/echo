import SceneKit

struct GeographicProbeResult {
    let coordinate: GeoPosition
    let local: SCNVector3
    let accuracy: Double
    let surfaceIntersections: Int
    let placement: String
}
