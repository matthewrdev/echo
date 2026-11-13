import Foundation

struct ImageFeature: Decodable {
    let x: Double
    let y: Double
    var kind: String? = nil
    let descriptor: [Double]
}
