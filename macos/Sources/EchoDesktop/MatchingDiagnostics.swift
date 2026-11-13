import Foundation

struct MatchingDiagnostics: Decodable {
    let featuresA: Int
    let featuresB: Int
    let accepted: Int
    let rejectedNonMutual: Int
    let rejectedAmbiguous: Int
    let rejectedDistance: Int
    let ratioThreshold: Double
    let distanceThreshold: Double
}
