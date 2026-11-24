import Foundation

struct GpsPolygon: Codable, Equatable, Identifiable {
    var id = UUID().uuidString
    var mode: CropRegionMode
    var vertices: [GpsPolygonVertex] = []
}
