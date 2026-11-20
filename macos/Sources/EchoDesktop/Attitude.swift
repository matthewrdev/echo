import Foundation

struct Attitude: Codable {
    var yaw: Double
    var pitch: Double
    var roll: Double
    var source: String? = nil
}
