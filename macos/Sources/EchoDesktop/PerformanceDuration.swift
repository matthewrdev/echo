import Foundation

enum PerformanceDuration {
    static func format(_ milliseconds: Double) -> String {
        if abs(milliseconds) >= 60_000 { return String(format: "%.2f min", milliseconds / 60_000) }
        if abs(milliseconds) >= 1000 { return String(format: "%.2f s", milliseconds / 1000) }
        return String(format: "%.2f ms", milliseconds)
    }
}
