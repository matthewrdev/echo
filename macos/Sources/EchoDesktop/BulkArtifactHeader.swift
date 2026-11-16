import Foundation

struct BulkArtifactHeader: Decodable {
    let storage: String
    let kind: String
    let imageId: String
    let width: Int
    let height: Int
    let voxelSize: Double
    let images: [String]
    let buffers: [String: String]
    let scene: ReconstructionArtifactMetadata?
}
