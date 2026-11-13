import AppKit

final class ImageCanvasRenderer: CanvasRenderer {
    private let image: NSImage
    var identity: String { String(ObjectIdentifier(image).hashValue) }
    var size: CGSize { image.size }

    init(image: NSImage) { self.image = image }
    func draw(in context: CGContext, scale: CGFloat) {
        image.draw(
            in: CGRect(origin: .zero, size: size), from: .zero,
            operation: .sourceOver, fraction: 1, respectFlipped: true, hints: nil)
    }
    func select(at point: CGPoint, tolerance: CGFloat) {}
}
