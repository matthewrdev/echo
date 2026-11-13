import Foundation

struct CloudPoint: Decodable {
    let position: [Double]
    let colour: Data?
    let error: Double
}
