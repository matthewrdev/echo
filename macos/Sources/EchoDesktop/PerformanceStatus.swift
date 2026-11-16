import Foundation

enum PerformanceStatus: String, Codable, CaseIterable {
    case running, completed, reused, cancelled, failed, interrupted
}
