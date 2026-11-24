import AppKit
import SwiftUI
import XCTest

@testable import EchoDesktop

final class NativeRunLogTests: XCTestCase {
    @MainActor
    func testLargeHistoryAppendsWithoutRebuildingTextOrLosingSelection() async throws {
        try WindowedTests.requireOptIn()

        let entries = (0..<10_000).map { index in
            RunLogEntry(id: index, timestamp: "2026-11-19T04:00:00Z", stage: "matching", message: "Pair \(index) ✓")
        }
        let host = NSHostingView(rootView: NativeRunLog(entries: entries, followLatest: .constant(false)))
        let window = NSWindow(
            contentRect: CGRect(x: 0, y: 0, width: 900, height: 220),
            styleMask: [.titled, .resizable], backing: .buffered, defer: false)
        window.contentView = host
        window.orderFront(nil)
        defer { window.orderOut(nil) }
        host.layoutSubtreeIfNeeded()
        try await Task.sleep(for: .milliseconds(50))
        let text = try XCTUnwrap(textView(in: host))
        XCTAssertFalse(text.isEditable)
        XCTAssertTrue(text.isSelectable)
        XCTAssertTrue(text.string.contains("Pair 0 ✓"))
        XCTAssertTrue(text.string.contains("Pair 9999 ✓"))
        let original = text.string
        let selection = (original as NSString).range(of: "Pair 0 ✓")
        text.setSelectedRange(selection)
        var edits = 0
        let observer = NotificationCenter.default.addObserver(
            forName: NSTextStorage.didProcessEditingNotification, object: text.textStorage, queue: .main
        ) { _ in edits += 1 }
        defer { NotificationCenter.default.removeObserver(observer) }

        let started = Date()
        for _ in 0..<100 {
            host.rootView = NativeRunLog(entries: entries, followLatest: .constant(false))
            host.layoutSubtreeIfNeeded()
            await Task.yield()
        }
        print("Native history: 10,000 entries, 100 refreshes in \(Date().timeIntervalSince(started)) seconds")
        XCTAssertEqual(edits, 0, "Unrelated progress updates must not rebuild the log document")
        XCTAssertEqual(text.selectedRange(), selection)
        let last = RunLogEntry(id: 10_000, timestamp: "2026-11-19T04:00:01Z", stage: "matching", message: "Finished 🪨")
        host.rootView = NativeRunLog(entries: entries + [last], followLatest: .constant(false))
        host.layoutSubtreeIfNeeded()
        await Task.yield()
        XCTAssertEqual(edits, 1, "Append only the new batch")
        XCTAssertEqual(text.string, original + last.time + "  Finished 🪨\n")
        XCTAssertEqual(text.selectedRange(), selection, "Reading and copying history must survive incoming progress")

        host.rootView = NativeRunLog(entries: [last], followLatest: .constant(true))
        host.layoutSubtreeIfNeeded()
        await Task.yield()
        XCTAssertEqual(
            text.string, last.time + "  Finished 🪨\n", "Switching stage or run replaces the displayed history")
    }

    @MainActor
    func testFollowingReachesTheEndAndPausedHistoryKeepsItsScrollPosition() async throws {
        try WindowedTests.requireOptIn()

        var entries = (0..<1000).map { index in
            RunLogEntry(id: index, timestamp: "2026-11-19T04:00:00Z", stage: "depth", message: "Depth image \(index)")
        }
        let host = NSHostingView(rootView: NativeRunLog(entries: entries, followLatest: .constant(true)))
        let window = NSWindow(
            contentRect: CGRect(x: 0, y: 0, width: 900, height: 220),
            styleMask: [.titled, .resizable], backing: .buffered, defer: false)
        window.contentView = host
        window.orderFront(nil)
        defer { window.orderOut(nil) }
        host.layoutSubtreeIfNeeded()
        try await Task.sleep(for: .milliseconds(50))
        let text = try XCTUnwrap(textView(in: host))
        let scroll = try XCTUnwrap(text.enclosingScrollView)
        XCTAssertGreaterThan(scroll.documentVisibleRect.minY, 1000)
        XCTAssertGreaterThanOrEqual(scroll.documentVisibleRect.maxY, text.bounds.maxY - 24)

        host.rootView = NativeRunLog(entries: entries, followLatest: .constant(false))
        host.layoutSubtreeIfNeeded()
        scroll.contentView.scroll(to: NSPoint(x: 0, y: 200))
        scroll.reflectScrolledClipView(scroll.contentView)
        let position = scroll.documentVisibleRect.minY
        entries.append(RunLogEntry(id: 1000, timestamp: "2026-11-19T04:00:01Z", stage: "depth", message: "Latest"))
        host.rootView = NativeRunLog(entries: entries, followLatest: .constant(false))
        host.layoutSubtreeIfNeeded()
        try await Task.sleep(for: .milliseconds(50))
        XCTAssertEqual(scroll.documentVisibleRect.minY, position, accuracy: 1)

        host.rootView = NativeRunLog(entries: entries, followLatest: .constant(true))
        host.layoutSubtreeIfNeeded()
        try await Task.sleep(for: .milliseconds(50))
        XCTAssertGreaterThanOrEqual(scroll.documentVisibleRect.maxY, text.bounds.maxY - 24)
    }

    @MainActor
    private func textView(in view: NSView) -> NSTextView? {
        if let text = view as? NSTextView { return text }
        return view.subviews.lazy.compactMap { self.textView(in: $0) }.first
    }
}
