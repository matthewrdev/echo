import MapKit

final class ScrollablePhotoMap: MKMapView {
    private var scrollMonitor: Any?

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        if let scrollMonitor { NSEvent.removeMonitor(scrollMonitor) }
        scrollMonitor = nil
        guard window != nil else { return }
        // MapKit's mouse wheel normally pans on macOS. Scope this override to this visible map only.
        scrollMonitor = NSEvent.addLocalMonitorForEvents(matching: .scrollWheel) { [weak self] event in
            guard let self, event.window === self.window,
                !self.isHiddenOrHasHiddenAncestor,
                self.bounds.contains(self.convert(event.locationInWindow, from: nil))
            else { return event }
            let delta = event.scrollingDeltaY
            guard abs(delta) > 0 else { return event }
            let anchor = self.convert(self.convert(event.locationInWindow, from: nil), toCoordinateFrom: self)
            let point = MKMapPoint(anchor)
            let old = self.visibleMapRect
            let factor = exp(-delta * (event.hasPreciseScrollingDeltas ? 0.012 : 0.12))
            let width = min(MKMapRect.world.width, max(0.25, old.width * factor))
            let ratio = width / old.width
            self.setVisibleMapRect(
                MKMapRect(
                    x: point.x - (point.x - old.minX) * ratio,
                    y: point.y - (point.y - old.minY) * ratio, width: width, height: old.height * ratio),
                animated: false)
            return nil
        }
    }

    deinit { if let scrollMonitor { NSEvent.removeMonitor(scrollMonitor) } }
}
