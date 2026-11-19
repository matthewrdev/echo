import AppKit
import SwiftUI

struct NativeRunLog: NSViewRepresentable {
    let entries: [RunLogEntry]
    @Binding var followLatest: Bool

    func makeCoordinator() -> RunLogTextCoordinator { RunLogTextCoordinator() }

    func makeNSView(context: Context) -> NSScrollView {
        let text = NSTextView(frame: NSRect(x: 0, y: 0, width: 600, height: 0))
        text.isEditable = false
        text.isSelectable = true
        text.isRichText = false
        text.usesFindBar = true
        text.drawsBackground = false
        text.isVerticallyResizable = true
        text.isHorizontallyResizable = false
        text.autoresizingMask = [.width]
        text.minSize = .zero
        text.maxSize = NSSize(width: CGFloat.greatestFiniteMagnitude, height: CGFloat.greatestFiniteMagnitude)
        text.textContainer?.widthTracksTextView = true
        text.textContainer?.containerSize = NSSize(width: 600, height: CGFloat.greatestFiniteMagnitude)
        text.textContainer?.lineFragmentPadding = 0
        text.textContainerInset = NSSize(width: 0, height: 6)
        text.layoutManager?.allowsNonContiguousLayout = true
        text.setAccessibilityLabel("Stage activity history")
        let scroll = NSScrollView()
        scroll.hasVerticalScroller = true
        scroll.autohidesScrollers = true
        scroll.drawsBackground = false
        scroll.documentView = text
        context.coordinator.scrolling.observe(text)
        return scroll
    }

    func updateNSView(_ scroll: NSScrollView, context: Context) {
        guard let text = scroll.documentView as? NSTextView else { return }
        context.coordinator.scrolling.update = { followLatest = $0 }
        context.coordinator.update(text: text, entries: entries, followLatest: followLatest)
    }

    static func dismantleNSView(_ scroll: NSScrollView, coordinator: RunLogTextCoordinator) {
        coordinator.scrolling.stop()
    }
}
