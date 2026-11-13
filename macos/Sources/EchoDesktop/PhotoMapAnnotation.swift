import MapKit

final class PhotoMapAnnotation: NSObject, MKAnnotation {
    let camera: MapCamera
    init(camera: MapCamera) { self.camera = camera }
    var coordinate: CLLocationCoordinate2D {
        CLLocationCoordinate2D(latitude: camera.position.latitude, longitude: camera.position.longitude)
    }
    var title: String? { camera.photo.name }
    var subtitle: String? {
        camera.pitchDegrees.map { "Tilt \($0.formatted(.number.precision(.fractionLength(1))))°" }
            ?? "Orientation unavailable"
    }
}
