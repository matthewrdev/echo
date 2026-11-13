import Foundation

struct MapCamera: Identifiable {
    let photo: Photo
    let position: GeoPosition
    let corners: [GeoPosition]
    let source: String
    var pitchDegrees: Double? = nil
    var id: String { photo.id }
}
