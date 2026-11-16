import Foundation

enum SparsePointDetail: String, CaseIterable {
    case automatic = "Auto"
    case all = "All"
    case custom = "Custom"

    func displayDetail(percentage: Double) -> CloudDisplayDetail {
        switch self {
        case .automatic: return .automatic
        case .all: return .percentage(100)
        case .custom: return .percentage(Int(percentage.rounded()))
        }
    }
}
