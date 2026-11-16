import Foundation

struct Photo: Decodable, Identifiable {
    let id: String
    let name: String
    let source: String
    let preview: String
    let width: Int
    let height: Int
    let capturedAt: String?
    let camera: String
    let focalPixels: Double
    let enabled: Bool
    let sets: [String]
    let gps: GeoPosition?
    let attitude: Attitude?
    let metadata: [String: JSONValue]

    var discarded: Bool? = nil
    var duplicateOf: String? = nil

    var isDiscarded: Bool { discarded == true }

    var hoverSummary: String {
        var lines = [name, "\(camera) · \(width) × \(height)"]
        if isDiscarded { lines.append("Discarded as a similar photo. Restore it to include it in processing.") }
        if let capturedAt, !capturedAt.isEmpty { lines.append("Captured: \(capturedAt)") }
        for key in ["ExposureTime", "FNumber", "ISO", "FocalLength"] {
            if let value = metadata[key] { lines.append("\(key): \(value.description)") }
        }
        if let gps { lines.append("GPS: \(gps.latitude), \(gps.longitude)") }
        if let altitude = gps?.altitude {
            lines.append("Altitude: \(altitude) m · reference: \(gps?.altitudeDatum ?? "unknown")")
        }
        if let attitude { lines.append("Yaw \(attitude.yaw)° · pitch \(attitude.pitch)°") }
        lines.append("Double click to open photograph and all metadata")
        return lines.joined(separator: "\n")
    }
}
