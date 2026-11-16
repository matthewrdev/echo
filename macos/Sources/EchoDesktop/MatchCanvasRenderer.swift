import AppKit

final class MatchCanvasRenderer: CanvasRenderer {
    private let data: MatchPresentation
    private let selected: Int?
    private let overlay: FeatureOverlayOptions
    private let selection: (Int) -> Void
    private let height = 900.0
    private let gap = 90.0
    private let top = 45.0
    var identity: String { data.first.id + data.second.id }
    private var firstWidth: Double { Double(data.first.width) / Double(data.first.height) * height }
    private var secondWidth: Double { Double(data.second.width) / Double(data.second.height) * height }
    var size: CGSize { CGSize(width: firstWidth + gap + secondWidth, height: height + top) }
    private var indices: [Int] { data.matches.indices.filter { overlay.shows(data.matches[$0]) } }

    init(
        data: MatchPresentation, selected: Int?, overlay: FeatureOverlayOptions = FeatureOverlayOptions(),
        selection: @escaping (Int) -> Void
    ) {
        self.data = data
        self.selected = selected
        self.overlay = overlay
        self.selection = selection
    }

    private func point(_ pixel: [Double], second: Bool) -> CGPoint {
        let photo = second ? data.second : data.first
        let width = second ? secondWidth : firstWidth
        return CGPoint(
            x: (second ? firstWidth + gap : 0) + pixel[0] / Double(photo.width) * width,
            y: top + pixel[1] / Double(photo.height) * height)
    }

    func draw(in context: CGContext, scale: CGFloat) {
        data.firstImage.draw(
            in: CGRect(x: 0, y: top, width: firstWidth, height: height),
            from: .zero, operation: .sourceOver, fraction: 1, respectFlipped: true, hints: nil)
        data.secondImage.draw(
            in: CGRect(x: firstWidth + gap, y: top, width: secondWidth, height: height),
            from: .zero, operation: .sourceOver, fraction: 1, respectFlipped: true, hints: nil)
        let attributes: [NSAttributedString.Key: Any] = [
            .font: NSFont.systemFont(ofSize: 23, weight: .semibold), .foregroundColor: NSColor.labelColor,
        ]
        ("A · " + data.first.name as NSString).draw(at: CGPoint(x: 8, y: 5), withAttributes: attributes)
        ("B · " + data.second.name as NSString).draw(
            at: CGPoint(x: firstWidth + gap + 8, y: 5), withAttributes: attributes)
        guard overlay.opacity > 0 else { return }
        context.saveGState()
        context.setAlpha(min(1, overlay.opacity))
        context.beginTransparencyLayer(auxiliaryInfo: nil)
        defer {
            context.endTransparencyLayer()
            context.restoreGState()
        }
        for index in indices.sorted(by: { ($0 == selected ? 1 : 0) < ($1 == selected ? 1 : 0) }) {
            let match = data.matches[index]
            let a = point(match.a, second: false)
            let b = point(match.b, second: true)
            let highlight = selected == index
            let color =
                match.manual
                ? NSColor.systemOrange
                : match.geometricInlier == false
                    ? NSColor.systemGray
                    : NSColor(calibratedHue: CGFloat(index % 19) / 19, saturation: 0.8, brightness: 1, alpha: 1)
            for second in [false, true] {
                if let line = second ? match.lineB : match.lineA, line.count == 2 {
                    let start = point(line[0], second: second)
                    let end = point(line[1], second: second)
                    for stroke in [false, true] {
                        context.setStrokeColor(stroke ? color.cgColor : NSColor.black.cgColor)
                        context.setLineWidth((stroke ? (highlight ? 3 : 1.5) : (highlight ? 5 : 3.5)) / scale)
                        context.move(to: start)
                        context.addLine(to: end)
                        context.strokePath()
                    }
                }
            }
            if highlight {
                drawConnection(from: a, to: b, color: color, in: context, scale: scale)
            }
            for position in [a, b] {
                let radius: CGFloat = (highlight ? 7 : 4) / scale
                let marker = CGRect(
                    x: position.x - radius, y: position.y - radius, width: radius * 2, height: radius * 2)
                context.setFillColor(color.cgColor)
                context.fillEllipse(in: marker)
                context.setStrokeColor(NSColor.black.cgColor)
                context.setLineWidth(1.5 / scale)
                context.strokeEllipse(in: marker)
                if highlight {
                    (" \(index + 1) " as NSString).draw(
                        at: CGPoint(x: position.x + radius + 2 / scale, y: position.y - 8 / scale),
                        withAttributes: [
                            .font: NSFont.monospacedDigitSystemFont(ofSize: 11 / scale, weight: .bold),
                            .foregroundColor: color, .backgroundColor: NSColor.black.withAlphaComponent(0.85),
                        ])
                }
            }
        }
    }

    private func drawConnection(from a: CGPoint, to b: CGPoint, color: NSColor, in context: CGContext, scale: CGFloat) {
        context.saveGState()
        defer { context.restoreGState() }
        context.setLineCap(.round)
        context.setLineDash(phase: 0, lengths: [0, 8 / scale])
        for stroke in [false, true] {
            context.setLineWidth((stroke ? 2.5 : 5) / scale)
            context.setStrokeColor((stroke ? color : NSColor.black).withAlphaComponent(0.9).cgColor)
            context.move(to: a)
            context.addLine(to: b)
            context.strokePath()
        }
    }

    func select(at position: CGPoint, tolerance: CGFloat) {
        let indices = self.indices
        var nearest: Int?
        var distance = tolerance
        for index in indices {
            for point in [point(data.matches[index].a, second: false), point(data.matches[index].b, second: true)] {
                let candidate = hypot(point.x - position.x, point.y - position.y)
                if candidate < distance {
                    nearest = index
                    distance = candidate
                }
            }
        }
        for index in indices {
            for second in [false, true] {
                let match = data.matches[index]
                if let line = second ? match.lineB : match.lineA, line.count == 2 {
                    let candidate = SegmentGeometry().distance(
                        position, from: point(line[0], second: second), to: point(line[1], second: second))
                    if candidate < distance {
                        nearest = index
                        distance = candidate
                    }
                }
            }
        }
        if let nearest { selection(nearest) }
    }
}
