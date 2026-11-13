import Foundation

struct StoredArtifact: Decodable, Identifiable {
    let path: String
    let stage: String
    let bytes: Int64
    let fileCount: Int
    let runs: [String]
    var id: String { path }
}
