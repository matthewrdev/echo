import Foundation

struct ImageMatch: Decodable {
    let a: [Double]
    let b: [Double]
    let score: Double?
    let manual: Bool
    var kind: String? = nil
    var lineA: [[Double]]? = nil
    var lineB: [[Double]]? = nil
}
