import Foundation

struct FeatureArtifactHeader: Decodable {
    let storage: String
    let imageId: String
    let payload: String
    let pointCount: Int
    let lineCount: Int
    let descriptorCount: Int
    let kinds: [String]
}
