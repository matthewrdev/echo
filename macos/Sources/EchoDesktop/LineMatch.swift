import Foundation

struct LineMatch: Decodable {
    let indexA: Int
    let indexB: Int
    let startA: [Double]
    let endA: [Double]
    let startB: [Double]
    let endB: [Double]
    let score: Double
    var displayMatch: ImageMatch {
        ImageMatch(
            a: [(startA[0] + endA[0]) / 2, (startA[1] + endA[1]) / 2],
            b: [(startB[0] + endB[0]) / 2, (startB[1] + endB[1]) / 2],
            score: score, manual: false, kind: "line", lineA: [startA, endA], lineB: [startB, endB])
    }
}
