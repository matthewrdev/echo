import EchoDrone
import MapKit

final class DroneMapPoint: NSObject, MKAnnotation {
    let id: URL
    let coordinate: CLLocationCoordinate2D
    let title: String?

    init(photo: DroneImportPhoto, coordinate: CLLocationCoordinate2D) {
        id = photo.id
        title = photo.name
        self.coordinate = coordinate
    }
}
