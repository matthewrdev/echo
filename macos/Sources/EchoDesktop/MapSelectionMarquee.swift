import AppKit

final class MapSelectionMarquee: NSView {
    private var anchor: CGPoint?
    var isDragging: Bool { anchor != nil }

    override func hitTest(_ point: NSPoint) -> NSView? { nil }

    func begin(at point: CGPoint, in view: NSView) {
        anchor = point
        frame = .zero
        if superview == nil { view.addSubview(self, positioned: .above, relativeTo: nil) }
    }

    func update(to point: CGPoint) {
        guard let anchor, let superview else { return }
        frame = CGRect(
            x: min(anchor.x, point.x), y: min(anchor.y, point.y),
            width: abs(point.x - anchor.x), height: abs(point.y - anchor.y)
        ).intersection(superview.bounds)
        needsDisplay = true
    }

    func finish(at point: CGPoint) -> CGRect? {
        update(to: point)
        let rect = frame
        cancel()
        return rect.width >= 4 && rect.height >= 4 ? rect : nil
    }

    func cancel() {
        anchor = nil
        removeFromSuperview()
    }

    override func draw(_ dirtyRect: NSRect) {
        NSColor.controlAccentColor.withAlphaComponent(0.16).setFill()
        bounds.fill()
        NSColor.controlAccentColor.setStroke()
        NSBezierPath(rect: bounds.insetBy(dx: 0.5, dy: 0.5)).stroke()
    }
}
