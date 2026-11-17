import AppKit
import simd

enum CropHandleKind: String {
    case move, width, depth, height, radius, rotate

    var help: String {
        switch self {
        case .move: return "Move centre on the X/Z plane"
        case .width: return "Resize width"
        case .depth: return "Resize depth"
        case .height: return "Resize height"
        case .radius: return "Resize radius"
        case .rotate: return "Rotate box around the vertical axis"
        }
    }

    var colour: NSColor {
        switch self {
        case .move: return .systemCyan
        case .rotate: return .systemYellow
        default: return .white
        }
    }

    var axis: SIMD3<Double> {
        switch self {
        case .depth: return SIMD3(0, 0, 1)
        case .height: return SIMD3(0, 1, 0)
        default: return SIMD3(1, 0, 0)
        }
    }
}
