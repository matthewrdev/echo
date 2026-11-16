import Foundation

struct PerformanceTraceDocument: Codable {
    let schemaVersion: Int
    let summary: PerformanceTraceSummary
    let spans: [PerformanceSpan]
    let statistics: [PerformanceStatistic]
}
