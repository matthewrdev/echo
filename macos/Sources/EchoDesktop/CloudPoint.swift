import Foundation

struct CloudPoint: Decodable {
    let position: [Double]
    let colour: Data?
    let error: Double
    var imageA: String? = nil
    var imageB: String? = nil
    var pixelA: [Double]? = nil
    var pixelB: [Double]? = nil
}
