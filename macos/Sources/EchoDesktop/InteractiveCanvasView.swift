import AppKit

final class InteractiveCanvasView: NSView {
    var renderer: CanvasRenderer? {
        didSet {
            if oldValue?.identity != renderer?.identity { fit() }
            needsDisplay = true
        }
    }
    var zoomChanged: ((Double) -> Void)?
    private var magnification: CGFloat = 1
    private var offset = CGPoint.zero
    private var dragDistance: CGFloat = 0
    override var isFlipped: Bool { true }
    override var acceptsFirstResponder: Bool { true }

    private var fitScale: CGFloat {
        guard let size = renderer?.size, size.width > 0, size.height > 0 else { return 1 }
        return max(0.001, min(bounds.width / size.width, bounds.height / size.height) * 0.96)
    }
    private var scale: CGFloat { fitScale * magnification }
    private var origin: CGPoint {
        let size = renderer?.size ?? .zero
        return CGPoint(
            x: (bounds.width - size.width * scale) / 2 + offset.x,
            y: (bounds.height - size.height * scale) / 2 + offset.y)
    }

    override func draw(_ dirtyRect: NSRect) {
        NSColor.controlBackgroundColor.setFill()
        bounds.fill()
        guard let context = NSGraphicsContext.current?.cgContext else { return }
        context.saveGState()
        context.clip(to: bounds)
        context.translateBy(x: origin.x, y: origin.y)
        context.scaleBy(x: scale, y: scale)
        renderer?.draw(in: context, scale: scale)
        context.restoreGState()
    }

    func fit() {
        magnification = 1
        offset = .zero
        zoomChanged?(1)
        needsDisplay = true
    }

    func zoom(by factor: CGFloat, at anchor: CGPoint? = nil) {
        let anchor = anchor ?? CGPoint(x: bounds.midX, y: bounds.midY)
        let oldOrigin = origin
        let position = CGPoint(x: (anchor.x - oldOrigin.x) / scale, y: (anchor.y - oldOrigin.y) / scale)
        magnification = min(40, max(0.25, magnification * factor))
        let size = renderer?.size ?? .zero
        offset = CGPoint(
            x: anchor.x - position.x * scale - (bounds.width - size.width * scale) / 2,
            y: anchor.y - position.y * scale - (bounds.height - size.height * scale) / 2)
        zoomChanged?(Double(magnification))
        needsDisplay = true
    }

    override func scrollWheel(with event: NSEvent) {
        let delta = event.scrollingDeltaY
        zoom(
            by: exp(-delta * (event.hasPreciseScrollingDeltas ? 0.015 : 0.08)),
            at: convert(event.locationInWindow, from: nil))
    }

    override func magnify(with event: NSEvent) {
        zoom(by: 1 + event.magnification, at: convert(event.locationInWindow, from: nil))
    }

    override func mouseDown(with event: NSEvent) {
        window?.makeFirstResponder(self)
        dragDistance = 0
        NSCursor.closedHand.push()
    }

    override func mouseDragged(with event: NSEvent) {
        offset.x += event.deltaX
        offset.y += event.deltaY
        dragDistance += abs(event.deltaX) + abs(event.deltaY)
        needsDisplay = true
    }

    override func mouseUp(with event: NSEvent) {
        NSCursor.pop()
        if dragDistance < 4 {
            let position = convert(event.locationInWindow, from: nil)
            renderer?.select(
                at: CGPoint(
                    x: (position.x - origin.x) / scale,
                    y: (position.y - origin.y) / scale), tolerance: 12 / scale)
        }
    }

    override func resetCursorRects() { addCursorRect(bounds, cursor: .openHand) }
}
