import Foundation

struct ImageMatch: Decodable {
    let a: [Double]
    let b: [Double]
    let score: Double?
    let manual: Bool
    var geometricInlier: Bool? = nil
    var kind: String? = nil
    var lineA: [[Double]]? = nil
    var lineB: [[Double]]? = nil
    var verificationLabel: String {
        if manual { return "Manual tie · supplied by you" }
        if kind == "line" { return "Unverified line candidate · excluded from alignment" }
        if geometricInlier == true { return "Passed pair geometry · 3D consistency checked during alignment" }
        if geometricInlier == false { return "Rejected by pair geometry · excluded from alignment" }
        return "Unverified point candidate · rerun Matching to save verification"
    }
}
