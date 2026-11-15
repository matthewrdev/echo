import AppKit

final class LogScrollPositionCoordinator {
    var update: (Bool) -> Void
    private weak var scrollView: NSScrollView?
    private var observation: NSObjectProtocol?

    init(update: @escaping (Bool) -> Void) { self.update = update }

    func observe(_ view: NSView) {
        guard let scroll = view.enclosingScrollView, scroll !== scrollView else { return }
        stop()
        scrollView = scroll
        scroll.contentView.postsBoundsChangedNotifications = true
        observation = NotificationCenter.default.addObserver(
            forName: NSView.boundsDidChangeNotification, object: scroll.contentView, queue: .main
        ) { [weak self, weak scroll] _ in
            guard let self, let scroll else { return }
            self.scrollChanged(in: scroll, event: NSApp.currentEvent?.type)
        }
    }

    func scrollChanged(in scroll: NSScrollView, event: NSEvent.EventType?) {
        guard event == .scrollWheel || event == .leftMouseDragged || event == .keyDown,
            let document = scroll.documentView
        else { return }
        let visible = scroll.documentVisibleRect
        let distance = document.isFlipped ? document.bounds.maxY - visible.maxY : visible.minY - document.bounds.minY
        let atBottom = distance <= 24
        DispatchQueue.main.async { self.update(atBottom) }
    }

    func stop() {
        if let observation { NotificationCenter.default.removeObserver(observation) }
        observation = nil
        scrollView = nil
    }

    deinit { stop() }
}
