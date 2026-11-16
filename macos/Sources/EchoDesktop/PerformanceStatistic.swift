import Foundation

struct PerformanceStatistic: Codable, Identifiable {
    let stage: String
    let name: String
    let kind: PerformanceSpanKind
    let backend: PerformanceBackend
    let count: Int64
    let reusedCount: Int64
    let failedCount: Int64
    let totalMilliseconds: Double
    let maximumMilliseconds: Double
    let gpuMilliseconds: Double?
    let items: Int64
    let bytes: Int64
    var id: String { "\(stage)/\(kind.rawValue)/\(backend.rawValue)/\(name)" }
    var backendLabel: String { backend.rawValue.uppercased() }
}
