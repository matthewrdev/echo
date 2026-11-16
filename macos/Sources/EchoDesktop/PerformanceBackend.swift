import Foundation

enum PerformanceBackend: String, Codable, CaseIterable {
    case cpu, metal, mixed
}
