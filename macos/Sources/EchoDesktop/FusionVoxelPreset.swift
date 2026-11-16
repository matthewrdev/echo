import Foundation

enum FusionVoxelPreset: String, CaseIterable, Identifiable {
    case automatic, twoPointFiveCentimetres, tenCentimetres, fiftyCentimetres, custom

    static let minimumMetres = 0.025

    static func clampedMetres(centimetres: Double) -> Double {
        guard centimetres.isFinite else { return minimumMetres }
        return max(minimumMetres, centimetres / 100)
    }

    static func needsDetailWarning(metres: Double) -> Bool {
        metres > 0 && metres < 0.1
    }

    var id: Self { self }

    var title: String {
        switch self {
        case .automatic: "Auto"
        case .twoPointFiveCentimetres: "2.5 cm"
        case .tenCentimetres: "10 cm"
        case .fiftyCentimetres: "50 cm"
        case .custom: "Custom"
        }
    }

    init(metres: Double) {
        switch metres {
        case 0: self = .automatic
        case Self.minimumMetres: self = .twoPointFiveCentimetres
        case 0.1: self = .tenCentimetres
        case 0.5: self = .fiftyCentimetres
        default: self = .custom
        }
    }

    var metres: Double? {
        switch self {
        case .automatic: 0
        case .twoPointFiveCentimetres: Self.minimumMetres
        case .tenCentimetres: 0.1
        case .fiftyCentimetres: 0.5
        case .custom: nil
        }
    }
}
