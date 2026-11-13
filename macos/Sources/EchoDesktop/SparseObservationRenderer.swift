import AppKit

final class SparseObservationRenderer: CanvasRenderer {
    let identity: String
    let size: CGSize
    private let image: NSImage
    private let pixel: [Double]

    init(image: NSImage, photo: Photo, pixel: [Double]) {
        self.image = image
        self.pixel = pixel
        identity = photo.id
        size = CGSize(width: photo.width, height: photo.height)
    }

    func draw(in context: CGContext, scale: CGFloat) {
        image.draw(
            in: CGRect(origin: .zero, size: size), from: .zero, operation: .sourceOver,
            fraction: 1, respectFlipped: true, hints: nil)
        let radius = 9 / scale
        let circle = CGRect(x: pixel[0] - radius, y: pixel[1] - radius, width: radius * 2, height: radius * 2)
        context.setStrokeColor(NSColor.black.cgColor)
        context.setLineWidth(5 / scale)
        context.strokeEllipse(in: circle)
        context.setStrokeColor(NSColor.systemYellow.cgColor)
        context.setLineWidth(2 / scale)
        context.strokeEllipse(in: circle)
        context.move(to: CGPoint(x: pixel[0] - radius * 2, y: pixel[1]))
        context.addLine(to: CGPoint(x: pixel[0] + radius * 2, y: pixel[1]))
        context.move(to: CGPoint(x: pixel[0], y: pixel[1] - radius * 2))
        context.addLine(to: CGPoint(x: pixel[0], y: pixel[1] + radius * 2))
        context.strokePath()
    }

    func select(at point: CGPoint, tolerance: CGFloat) {}
}
