import Foundation

struct MatchingIndexEntry: Codable {
    let sequence: Int
    let artifact: String
    let a: String
    let b: String
    let pointCount: Int
    let lineCount: Int
}
