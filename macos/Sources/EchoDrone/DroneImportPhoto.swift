import CoreLocation
import Foundation

package struct DroneImportPhoto: Identifiable, Sendable {
    package let file: DroneMediaFile
    package let capturedAt: Date?
    package let coordinate: CLLocationCoordinate2D?

    package var id: URL { file.id }
    package var name: String { file.url.lastPathComponent }
}
