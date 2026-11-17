import Foundation

struct PerformanceTraceDocument: Codable {
    let schemaVersion: Int
    let summary: PerformanceTraceSummary
    let spans: [PerformanceSpan]
    let statistics: [PerformanceStatistic]

    var reusedStages: Set<String> {
        Set(spans.filter { $0.kind == .stage && isReused($0) }.map(\.stage))
    }

    func isReused(_ span: PerformanceSpan) -> Bool {
        if span.status == .reused { return true }
        guard span.status == .completed else { return false }
        let checkpoints = statistics.filter { $0.stage == span.stage && $0.kind == .checkpoint }
        let total = checkpoints.reduce(0) { $0 + $1.count }
        return total > 0 && checkpoints.reduce(0) { $0 + $1.reusedCount } == total
    }
}
