import CoreLocation
import Foundation

struct DroneImportPhoto: Identifiable, Sendable {
    let file: DroneMediaFile
    let capturedAt: Date?
    let coordinate: CLLocationCoordinate2D?

    var id: URL { file.id }
    var name: String { file.url.lastPathComponent }
}
