import Foundation

package struct GeoPosition: Codable {
    package init(
        latitude: Double, longitude: Double, altitude: Double? = nil, altitudeDatum: String? = nil,
        uncertaintyMetres: Double? = nil
    ) {
        self.latitude = latitude
        self.longitude = longitude
        self.altitude = altitude
        self.altitudeDatum = altitudeDatum
        self.uncertaintyMetres = uncertaintyMetres
    }

    package var latitude: Double
    package var longitude: Double
    package var altitude: Double?
    package var altitudeDatum: String?
    package var uncertaintyMetres: Double?
}
