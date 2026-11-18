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
    var busyMilliseconds: Double? = nil
    var maximumConcurrency: Int? = nil
    var medianUpperMilliseconds: Double? = nil
    var p95UpperMilliseconds: Double? = nil
    var threadAllocatedBytes: Int64? = nil
    var threadAllocationSamples: Int64? = nil
    var processAllocatedBytes: Int64? = nil
    var processCpuMilliseconds: Double? = nil
    var gcPauseMilliseconds: Double? = nil
    var gen0Collections: Int64? = nil
    var gen1Collections: Int64? = nil
    var gen2Collections: Int64? = nil
    var id: String { "\(stage)/\(kind.rawValue)/\(backend.rawValue)/\(name)" }
    var backendLabel: String { backend.rawValue.uppercased() }
}
