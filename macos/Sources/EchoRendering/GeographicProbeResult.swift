import SceneKit

package struct GeographicProbeResult {
    package let coordinate: GeoPosition
    package let local: SCNVector3
    package let accuracy: Double
    package let surfaceIntersections: Int
    package let placement: String

    package init(
        coordinate: GeoPosition, local: SCNVector3, accuracy: Double, surfaceIntersections: Int, placement: String
    ) {
        self.coordinate = coordinate
        self.local = local
        self.accuracy = accuracy
        self.surfaceIntersections = surfaceIntersections
        self.placement = placement
    }
}
