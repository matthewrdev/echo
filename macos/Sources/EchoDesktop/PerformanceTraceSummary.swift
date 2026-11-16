import Foundation

struct PerformanceTraceSummary: Codable, Identifiable {
    let id: String
    let runId: String
    let target: String
    let startedAt: String
    let updatedAt: String
    let status: PerformanceStatus
    let durationMilliseconds: Double
    let cpuMilliseconds: Double
    let droppedSpans: Int

    var label: String {
        let date = String(startedAt.prefix(19)).replacingOccurrences(of: "T", with: " ")
        return "\(target.capitalized) · \(date) UTC · \(runId.prefix(6))/\(id.prefix(6))"
    }
}
