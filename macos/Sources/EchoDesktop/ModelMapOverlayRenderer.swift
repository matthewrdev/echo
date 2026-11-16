import MapKit

final class ModelMapOverlayRenderer: MKOverlayRenderer {
    private let image: CGImage?

    init(model: ModelMapOverlay) {
        image = model.image.cgImage(forProposedRect: nil, context: nil, hints: nil)
        super.init(overlay: model)
    }

    override func draw(_ mapRect: MKMapRect, zoomScale: MKZoomScale, in context: CGContext) {
        guard let image else { return }
        let target = rect(for: overlay.boundingMapRect)
        context.saveGState()
        context.translateBy(x: target.minX, y: target.maxY)
        context.scaleBy(x: 1, y: -1)
        context.draw(image, in: CGRect(origin: .zero, size: target.size))
        context.restoreGState()
    }
}
