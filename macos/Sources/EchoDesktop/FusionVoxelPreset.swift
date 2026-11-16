import Foundation

enum FusionVoxelPreset: String, CaseIterable, Identifiable {
    case automatic, tenCentimetres, fiftyCentimetres, custom

    var id: Self { self }

    var title: String {
        switch self {
        case .automatic: "Auto"
        case .tenCentimetres: "10 cm"
        case .fiftyCentimetres: "50 cm"
        case .custom: "Custom"
        }
    }

    init(metres: Double) {
        switch metres {
        case 0: self = .automatic
        case 0.1: self = .tenCentimetres
        case 0.5: self = .fiftyCentimetres
        default: self = .custom
        }
    }

    var metres: Double? {
        switch self {
        case .automatic: 0
        case .tenCentimetres: 0.1
        case .fiftyCentimetres: 0.5
        case .custom: nil
        }
    }
}
