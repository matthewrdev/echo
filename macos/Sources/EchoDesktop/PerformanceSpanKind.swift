import Foundation

enum PerformanceSpanKind: String, Codable, CaseIterable {
    case stage, checkpoint, operation, compute, transfer, wait
}
