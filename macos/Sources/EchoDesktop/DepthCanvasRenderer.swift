import AppKit

final class DepthCanvasRenderer: CanvasRenderer {
    private let data: DepthImagePresentation
    private let overlayOpacity: Double
    var identity: String { data.identity }
    var size: CGSize { data.overlay.size }

    init(data: DepthImagePresentation, overlayOpacity: Double) {
        self.data = data
        self.overlayOpacity = min(1, max(0, overlayOpacity))
    }

    func draw(in context: CGContext, scale: CGFloat) {
        let bounds = CGRect(origin: .zero, size: size)
        context.setFillColor(NSColor(srgbRed: 0.02, green: 0.02, blue: 0.08, alpha: 1).cgColor)
        context.fill(bounds)
        if let photograph = data.photograph {
            photograph.draw(
                in: bounds, from: .zero, operation: .sourceOver, fraction: 1,
                respectFlipped: true, hints: nil)
        }
        data.overlay.draw(
            in: bounds, from: .zero, operation: .sourceOver, fraction: overlayOpacity,
            respectFlipped: true, hints: [.interpolation: NSImageInterpolation.none])
    }

    func select(at point: CGPoint, tolerance: CGFloat) {}
}
