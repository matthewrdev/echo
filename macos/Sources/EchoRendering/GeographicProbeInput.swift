import Foundation

package struct GeographicProbeInput {
    package let latitude: Double
    package let longitude: Double
    package let altitude: Double?
    package let accuracy: Double

    package init(latitude: String, longitude: String, altitude: String, accuracy: String) throws {
        guard let latitude = Double(latitude.trimmingCharacters(in: .whitespacesAndNewlines)),
            latitude.isFinite, (-90...90).contains(latitude),
            let longitude = Double(longitude.trimmingCharacters(in: .whitespacesAndNewlines)),
            longitude.isFinite, (-180...180).contains(longitude)
        else {
            throw ModelRenderError(message: "Enter valid decimal latitude (−90 to 90) and longitude (−180 to 180).")
        }
        let height = altitude.trimmingCharacters(in: .whitespacesAndNewlines)
        let parsedHeight = height.isEmpty ? nil : Double(height)
        guard height.isEmpty || parsedHeight?.isFinite == true else {
            throw ModelRenderError(
                message: "Altitude must be a finite number of metres, or left blank for surface placement.")
        }
        guard let accuracy = Double(accuracy.trimmingCharacters(in: .whitespacesAndNewlines)),
            accuracy.isFinite, (0...100_000).contains(accuracy)
        else { throw ModelRenderError(message: "Horizontal accuracy must be between 0 and 100,000 metres.") }
        self.latitude = latitude
        self.longitude = longitude
        self.altitude = parsedHeight
        self.accuracy = accuracy
    }
}
