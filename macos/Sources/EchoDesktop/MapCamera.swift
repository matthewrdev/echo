import EchoRendering
import Foundation

struct MapCamera: Identifiable {
    let photo: Photo
    let position: GeoPosition
    let corners: [GeoPosition]
    let source: String
    var pitchDegrees: Double? = nil
    var id: String { photo.id }

    func displayCorners(selected: Bool) -> [GeoPosition] {
        guard selected else { return corners }
        let scale = CameraFrustumDisplay.selectedScale
        return corners.map { corner in
            var displayed = corner
            displayed.latitude = position.latitude + (corner.latitude - position.latitude) * scale
            displayed.longitude = position.longitude + (corner.longitude - position.longitude) * scale
            if let originAltitude = position.altitude, let altitude = corner.altitude {
                displayed.altitude = originAltitude + (altitude - originAltitude) * scale
            }
            return displayed
        }
    }
}
