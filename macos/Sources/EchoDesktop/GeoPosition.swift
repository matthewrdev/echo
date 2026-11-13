import Foundation

struct GeoPosition: Codable {
    var latitude: Double
    var longitude: Double
    var altitude: Double?
    var altitudeDatum: String?
    var uncertaintyMetres: Double?
}
