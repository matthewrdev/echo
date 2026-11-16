import Foundation

enum CloudDisplayDetail {
    case automatic
    case pointLimit(Int)
    case percentage(Int)

    // SceneKit's tile geometry allocation can fail long before a vertex buffer
    // reaches Metal's memory limit. Leave room for both frustum highlight passes.
    static let automaticPointLimit = 1_000_000
    static let maximumPointLimit = 4_000_000

    func limit(for total: Int) -> Int {
        let total = max(0, total)
        let requested: Int
        switch self {
        case .automatic: requested = Self.automaticPointLimit
        case .pointLimit(let count): requested = max(1, count)
        case .percentage(let percent):
            let percent = min(100, max(1, percent))
            requested = max(1, total / 100 * percent + total % 100 * percent / 100)
        }
        return min(total, requested, Self.maximumPointLimit)
    }
}
