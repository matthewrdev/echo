import Foundation

struct ImagePair: Decodable {
    let a: String
    let b: String
    let matches: [ImageMatch]
    let lineMatches: [LineMatch]?
    var displayMatches: [ImageMatch] { matches + (lineMatches ?? []).map(\.displayMatch) }
    let diagnostics: MatchingDiagnostics?
}
