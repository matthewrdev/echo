import EchoRendering
import Foundation

struct ImageHint: Codable, Identifiable {
    var id: String = UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased()
    var type: String
    var imageId: String
    var otherImageId: String?
    var position: GeoPosition?
    var attitude: Attitude?
    var pixel: [Double]?
    var otherPixel: [Double]?
    var enabled: Bool = true
    var note: String = ""
}
