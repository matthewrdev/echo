import AppKit

final class PatchCanvasRenderer: CanvasRenderer {
    let identity: String
    private let patch: ImagePatch
    var size: CGSize { patch.image.size }

    init(identity: String, patch: ImagePatch) {
        self.identity = identity
        self.patch = patch
    }

    func draw(in context: CGContext, scale: CGFloat) {
        patch.image.draw(
            in: CGRect(origin: .zero, size: size), from: .zero,
            operation: .sourceOver, fraction: 1, respectFlipped: true, hints: nil)
        let radius: CGFloat = 6 / scale
        let marker = CGRect(
            x: patch.centre.x - radius, y: patch.centre.y - radius, width: radius * 2, height: radius * 2)
        context.setLineWidth(3 / scale)
        context.setStrokeColor(NSColor.black.cgColor)
        context.strokeEllipse(in: marker)
        context.setLineWidth(1.5 / scale)
        context.setStrokeColor(NSColor.systemYellow.cgColor)
        context.strokeEllipse(in: marker)
    }
    func select(at point: CGPoint, tolerance: CGFloat) {}
}
