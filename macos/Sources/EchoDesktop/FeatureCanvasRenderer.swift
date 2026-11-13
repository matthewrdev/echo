import AppKit

final class FeatureCanvasRenderer: CanvasRenderer {
    private let data: FeaturePresentation
    private let selected: Int?
    private let selection: (Int) -> Void
    var identity: String { data.photo.id }
    var size: CGSize { CGSize(width: data.photo.width, height: data.photo.height) }

    init(data: FeaturePresentation, selected: Int?, selection: @escaping (Int) -> Void) {
        self.data = data
        self.selected = selected
        self.selection = selection
    }

    func draw(in context: CGContext, scale: CGFloat) {
        data.image.draw(
            in: CGRect(origin: .zero, size: size), from: .zero,
            operation: .sourceOver, fraction: 1, respectFlipped: true, hints: nil)
        let matched = Set(data.matches.map(\.featureIndex))
        for (index, line) in data.lines.enumerated() {
            let featureIndex = data.features.count + index
            let color =
                selected == featureIndex
                ? NSColor.systemOrange : matched.contains(featureIndex) ? .systemMint : .systemCyan
            for stroke in [false, true] {
                context.setStrokeColor(stroke ? color.cgColor : NSColor.black.cgColor)
                context.setLineWidth((stroke ? 1.8 : 4) / scale)
                context.move(to: CGPoint(x: line.start[0], y: line.start[1]))
                context.addLine(to: CGPoint(x: line.end[0], y: line.end[1]))
                context.strokePath()
            }
        }
        for (index, feature) in data.features.enumerated() {
            let highlighted = index == selected
            let radius: CGFloat = (highlighted ? 8 : 3.5) / scale
            let marker = CGRect(x: feature.x - radius, y: feature.y - radius, width: 2 * radius, height: 2 * radius)
            let color =
                highlighted ? NSColor.systemOrange : matched.contains(index) ? NSColor.systemMint : NSColor.systemYellow
            context.setStrokeColor(NSColor.black.cgColor)
            context.setLineWidth((highlighted ? 4 : 3) / scale)
            context.strokeEllipse(in: marker)
            context.setStrokeColor(color.cgColor)
            context.setLineWidth((highlighted ? 2.5 : 1.4) / scale)
            context.strokeEllipse(in: marker)
            if feature.kind == "junction" {
                context.stroke(
                    CGRect(
                        x: feature.x - radius * 1.5, y: feature.y - radius * 1.5,
                        width: radius * 3, height: radius * 3))
            }
            if highlighted {
                (" Feature \(index + 1) " as NSString).draw(
                    at: CGPoint(x: feature.x + radius, y: feature.y - 10 / scale),
                    withAttributes: [
                        .font: NSFont.systemFont(ofSize: 12 / scale, weight: .bold),
                        .foregroundColor: NSColor.white, .backgroundColor: NSColor.black,
                    ])
            }
        }
    }

    func select(at point: CGPoint, tolerance: CGFloat) {
        var nearest: Int?
        var distance = tolerance
        for (index, feature) in data.features.enumerated() {
            let candidate = hypot(point.x - feature.x, point.y - feature.y)
            if candidate < distance {
                nearest = index
                distance = candidate
            }
        }
        for (index, line) in data.lines.enumerated() {
            let candidate = SegmentGeometry().distance(
                point, from: CGPoint(x: line.start[0], y: line.start[1]),
                to: CGPoint(x: line.end[0], y: line.end[1]))
            if candidate < distance {
                nearest = data.features.count + index
                distance = candidate
            }
        }
        if let nearest { selection(nearest) }
    }
}
