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
        let path = CGMutablePath()
        switch patch.overlay {
        case .line(let start, let end):
            path.move(to: start)
            path.addLine(to: end)
            for endpoint in [start, end] {
                path.addEllipse(
                    in: CGRect(
                        x: endpoint.x - radius / 2, y: endpoint.y - radius / 2,
                        width: radius, height: radius))
            }
        case .point(let kind):
            if kind == "junction" || kind == "corner" {
                path.addRect(marker)
            } else {
                path.addEllipse(in: marker)
            }
        }
        for outline in [true, false] {
            context.setLineWidth((outline ? 4 : 2) / scale)
            context.setStrokeColor(outline ? NSColor.black.cgColor : NSColor.systemYellow.cgColor)
            context.addPath(path)
            context.strokePath()
        }
    }
    func select(at point: CGPoint, tolerance: CGFloat) {}
}
