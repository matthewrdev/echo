import Foundation

struct ArtifactStorageInventory: Decodable {
    let bytes: Int64
    let runCount: Int
    let artifacts: [StoredArtifact]
}
