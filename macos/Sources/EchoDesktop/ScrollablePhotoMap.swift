import MapKit

final class ScrollablePhotoMap: MKMapView {
    private var eventMonitor: Any?
    private let marquee = MapSelectionMarquee()
    var boxSelection: ((CGRect) -> Void)?

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        if let eventMonitor { NSEvent.removeMonitor(eventMonitor) }
        eventMonitor = nil
        marquee.cancel()
        guard window != nil else { return }
        // MapKit's mouse wheel normally pans on macOS. Scope this override to this visible map only.
        eventMonitor = NSEvent.addLocalMonitorForEvents(
            matching: [.scrollWheel, .leftMouseDown, .leftMouseDragged, .leftMouseUp]
        ) { [weak self] event in
            guard let self else { return event }
            return handleMapEvent(event)
        }
    }

    func handleMapEvent(_ event: NSEvent) -> NSEvent? {
        guard event.window === window, !isHiddenOrHasHiddenAncestor else { return event }
        let point = convert(event.locationInWindow, from: nil)
        if marquee.isDragging {
            switch event.type {
            case .leftMouseDragged:
                marquee.update(to: point)
                return nil
            case .leftMouseUp:
                if let rect = marquee.finish(at: point) { boxSelection?(rect) }
                return nil
            default:
                return nil
            }
        }
        guard bounds.contains(point) else { return event }
        if event.type == .leftMouseDown, boxSelection != nil, event.modifierFlags.contains(.shift),
            let hit = window?.contentView?.hitTest(event.locationInWindow), hit === self || hit.isDescendant(of: self)
        {
            marquee.begin(at: point, in: self)
            return nil
        }
        guard event.type == .scrollWheel else { return event }
        let delta = event.scrollingDeltaY
        guard abs(delta) > 0 else { return event }
        let anchor = MKMapPoint(convert(point, toCoordinateFrom: self))
        let old = visibleMapRect
        let factor = exp(-delta * (event.hasPreciseScrollingDeltas ? 0.012 : 0.12))
        let width = min(MKMapRect.world.width, max(0.25, old.width * factor))
        let ratio = width / old.width
        setVisibleMapRect(
            MKMapRect(
                x: anchor.x - (anchor.x - old.minX) * ratio,
                y: anchor.y - (anchor.y - old.minY) * ratio, width: width, height: old.height * ratio),
            animated: false)
        return nil
    }

    deinit { if let eventMonitor { NSEvent.removeMonitor(eventMonitor) } }
}
