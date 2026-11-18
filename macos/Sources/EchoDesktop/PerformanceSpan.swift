import Foundation

struct PerformanceSpan: Codable, Identifiable {
    let id: Int64
    let parentId: Int64?
    let stage: String
    let name: String
    let kind: PerformanceSpanKind
    let backend: PerformanceBackend
    let status: PerformanceStatus
    let threadId: Int
    let startMilliseconds: Double
    let durationMilliseconds: Double
    let gpuMilliseconds: Double?
    let items: Int64?
    let totalItems: Int64?
    let unit: String
    let bytes: Int64
    let detail: String?
    var threadAllocatedBytes: Int64? = nil
    var processAllocatedBytes: Int64? = nil
    var processCpuMilliseconds: Double? = nil
    var gcPauseMilliseconds: Double? = nil
    var gen0Collections: Int64? = nil
    var gen1Collections: Int64? = nil
    var gen2Collections: Int64? = nil
}
