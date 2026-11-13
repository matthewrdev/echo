import Foundation

struct DepthMap: Decodable {
    let imageId: String
    let width: Int
    let height: Int
    let depth: [Float]
    let confidence: [Float]
}
