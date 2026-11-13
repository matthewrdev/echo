import Foundation

enum CloudDisplayDetail {
    case pointLimit(Int)
    case percentage(Int)

    func limit(for total: Int) -> Int {
        switch self {
        case .pointLimit(let count): return min(total, max(1, count))
        case .percentage(let percent):
            return min(total, max(1, Int(Int64(total) * Int64(min(100, max(1, percent))) / 100)))
        }
    }
}
