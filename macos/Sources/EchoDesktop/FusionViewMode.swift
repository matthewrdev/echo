import EchoRendering

enum FusionViewMode: String, CaseIterable {
    case surface = "Surface"
    case support = "Support"
    case sliceX = "Slice X"
    case sliceY = "Slice Y"
    case sliceZ = "Slice Z"

    init(selection: String) {
        self = Self(rawValue: selection) ?? Self(rawValue: "Slice \(selection)") ?? .surface
    }

    var axis: String? {
        switch self {
        case .sliceX: return "X"
        case .sliceY: return "Y"
        case .sliceZ: return "Z"
        default: return nil
        }
    }
}
