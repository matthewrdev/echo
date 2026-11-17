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

    var startDate: Date? {
        let parser = ISO8601DateFormatter()
        parser.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        var date = parser.date(from: startedAt)
        if date == nil {
            parser.formatOptions = [.withInternetDateTime]
            date = parser.date(from: startedAt)
        }
        return date
    }

    var label: String {
        let timestamp = startDate?.formatted(date: .abbreviated, time: .standard) ?? startedAt
        return "\(target.capitalized) · \(timestamp)"
    }
}
