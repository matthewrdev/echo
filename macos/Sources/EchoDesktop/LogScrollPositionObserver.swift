import AppKit
import SwiftUI

struct LogScrollPositionObserver: NSViewRepresentable {
    @Binding var followLatest: Bool

    func makeCoordinator() -> LogScrollPositionCoordinator {
        LogScrollPositionCoordinator { followLatest = $0 }
    }

    func makeNSView(context: Context) -> NSView {
        let view = NSView()
        DispatchQueue.main.async { context.coordinator.observe(view) }
        return view
    }

    func updateNSView(_ view: NSView, context: Context) {
        context.coordinator.update = { followLatest = $0 }
        DispatchQueue.main.async { context.coordinator.observe(view) }
    }

    static func dismantleNSView(_ view: NSView, coordinator: LogScrollPositionCoordinator) {
        coordinator.stop()
    }
}
