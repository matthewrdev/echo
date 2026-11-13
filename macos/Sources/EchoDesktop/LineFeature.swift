import Foundation

struct LineFeature: Decodable {
    let start: [Double]
    let end: [Double]
    let descriptor: [Double]
    let strength: Double
    var midpoint: [Double] { [(start[0] + end[0]) / 2, (start[1] + end[1]) / 2] }
    var length: Double { hypot(end[0] - start[0], end[1] - start[1]) }
}
