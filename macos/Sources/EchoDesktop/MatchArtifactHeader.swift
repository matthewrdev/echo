import Foundation

struct MatchArtifactHeader: Decodable {
    let storage: String
    let a: String
    let b: String
    let payload: String
    let pointCount: Int
    let lineCount: Int
    let kinds: [String]
    let diagnostics: MatchingDiagnostics?
}
