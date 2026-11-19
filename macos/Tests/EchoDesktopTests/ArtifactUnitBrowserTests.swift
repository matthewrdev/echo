import AppKit
import SwiftUI
import XCTest

@testable import EchoDesktop

final class ArtifactUnitBrowserTests: XCTestCase {
    @MainActor
    func testLargePairBrowserReusesVisibleRowsAndKeepsSelectionAcrossRefreshes() async throws {
        let units = (0..<10_000).map { index in
            ArtifactUnit(
                id: "pair-\(index)", title: "Photograph \(index) ↔ Photograph \(index + 1)",
                file: URL(fileURLWithPath: "/pairs/\(index).json"), detail: "1200 candidates")
        }
        var selection = "pair-0"
        let host = NSHostingView(
            rootView: ArtifactUnitBrowser(units: units, selected: selection) { selection = $0 })
        let window = NSWindow(
            contentRect: CGRect(x: 0, y: 0, width: 210, height: 700),
            styleMask: [.titled, .resizable], backing: .buffered, defer: false)
        window.contentView = host
        window.orderFront(nil)
        defer { window.orderOut(nil) }
        host.layoutSubtreeIfNeeded()
        try await Task.sleep(for: .milliseconds(100))

        let table = try XCTUnwrap(table(in: host), "The large browser must use native row virtualization")
        XCTAssertEqual(table.numberOfRows, units.count)
        XCTAssertLessThan(table.rows(in: table.visibleRect).length, 20)
        table.scrollRowToVisible(units.count - 1)
        table.selectRowIndexes(IndexSet(integer: units.count - 1), byExtendingSelection: false)
        try await Task.sleep(for: .milliseconds(100))
        XCTAssertEqual(selection, "pair-9999", "Native selection still opens the selected artifact")

        for update in 0..<10 {
            host.rootView = ArtifactUnitBrowser(
                units: units, selected: selection, selection: { selection = $0 }, footer: "Update \(update)")
            host.layoutSubtreeIfNeeded()
            await Task.yield()
        }
        XCTAssertEqual(table.selectedRow, units.count - 1)
        var residentRows = 0
        table.enumerateAvailableRowViews { _, _ in residentRows += 1 }
        XCTAssertLessThan(residentRows, 50, "Processing refreshes must not instantiate thousands of offscreen rows")
    }

    @MainActor
    private func table(in view: NSView) -> NSTableView? {
        if let table = view as? NSTableView { return table }
        return view.subviews.lazy.compactMap { self.table(in: $0) }.first
    }
}
