import Foundation

struct TextureArtifact: Decodable {
    let uvs: [[Double]]
    let size: Int
    let meshFile: String?
    let chartCount: Int?
    let coverageFraction: Double?
}
