import Foundation

struct TextureArtifact: Decodable {
    var uvsFile: String? = nil
    let uvs: [[Double]]
    let size: Int
    let meshFile: String?
    let chartCount: Int?
    let coverageFraction: Double?
}
