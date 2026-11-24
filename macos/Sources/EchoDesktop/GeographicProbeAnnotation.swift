import EchoRendering
import MapKit

final class GeographicProbeAnnotation: NSObject, MKAnnotation {
    let coordinate: CLLocationCoordinate2D
    let title: String? = "Test location"
    let subtitle: String?

    init(position: GeoPosition, accuracy: Double) {
        coordinate = CLLocationCoordinate2D(latitude: position.latitude, longitude: position.longitude)
        subtitle = String(format: "±%.1f m horizontal accuracy", accuracy)
    }
}
