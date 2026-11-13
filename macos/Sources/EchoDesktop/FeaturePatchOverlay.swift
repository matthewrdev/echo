import AppKit

enum FeaturePatchOverlay {
    case point(kind: String?)
    case line(start: CGPoint, end: CGPoint)

    init(line: [[Double]]?) {
        if let line, line.count == 2, line[0].count == 2, line[1].count == 2 {
            self = .line(start: CGPoint(x: line[0][0], y: line[0][1]), end: CGPoint(x: line[1][0], y: line[1][1]))
        } else {
            self = .point(kind: nil)
        }
    }

    var title: String {
        switch self {
        case .line: return "Line segment"
        case .point(let kind):
            switch kind {
            case "junction": return "Line junction"
            case "scale-space": return "Scale-space point"
            case "corner": return "Corner"
            default: return "Point feature"
            }
        }
    }

    func transformed(scaleX: Double, scaleY: Double, origin: CGPoint) -> FeaturePatchOverlay {
        switch self {
        case .point: return self
        case .line(let start, let end):
            return .line(
                start: CGPoint(x: start.x * scaleX - origin.x, y: start.y * scaleY - origin.y),
                end: CGPoint(x: end.x * scaleX - origin.x, y: end.y * scaleY - origin.y))
        }
    }
}
