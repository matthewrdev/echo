import AppKit
import SwiftUI
import XCTest

@testable import EchoDesktop

final class PersistentSplitViewTests: XCTestCase {
    @MainActor
    func testSidebarSizeSurvivesReopeningAndWindowResizing() async throws {
        try await verifySavedSize(layout: .sidebar, defaultLength: 280, position: 340, savedLength: 340)
    }

    @MainActor
    func testActivitySizeSurvivesReopeningAndWindowResizing() async throws {
        try await verifySavedSize(layout: .activity, defaultLength: 160, position: 579, savedLength: 220)
    }

    @MainActor
    func testCollapsingAndTemporarilyClampingActivityPreserveExpandedHeight() async throws {
        let coordinator = PersistentSplitCoordinator(layout: .activity, paneLength: 300)
        let split = makeSplit(layout: .activity, coordinator: coordinator)
        coordinator.onResize = { _ in XCTFail("Layout changes must not overwrite the saved height") }
        XCTAssertEqual(split.arrangedSubviews[1].frame.height, 300, accuracy: 1)

        coordinator.update(split, paneLength: 300, collapsedLength: 36)
        XCTAssertEqual(split.arrangedSubviews[1].frame.height, 36, accuracy: 1)
        split.setFrameSize(NSSize(width: 1200, height: 900))
        XCTAssertEqual(split.arrangedSubviews[1].frame.height, 36, accuracy: 1)
        coordinator.update(split, paneLength: 300, collapsedLength: nil)
        XCTAssertEqual(split.arrangedSubviews[1].frame.height, 300, accuracy: 1)

        split.setFrameSize(NSSize(width: 1200, height: 400))
        XCTAssertEqual(split.arrangedSubviews[0].frame.height, 180, accuracy: 1)
        XCTAssertEqual(split.arrangedSubviews[1].frame.height, 219, accuracy: 1)
        split.setFrameSize(NSSize(width: 1200, height: 800))
        XCTAssertEqual(split.arrangedSubviews[1].frame.height, 300, accuracy: 1)
        try await Task.sleep(for: .milliseconds(100))
    }

    @MainActor
    func testDraggingRespectsSidebarAndViewerMinimums() {
        let coordinator = PersistentSplitCoordinator(layout: .sidebar, paneLength: 280)
        let split = makeSplit(layout: .sidebar, coordinator: coordinator)
        split.setPosition(0, ofDividerAt: 0)
        XCTAssertEqual(split.arrangedSubviews[0].frame.width, 250, accuracy: 1)
        split.setPosition(1100, ofDividerAt: 0)
        XCTAssertEqual(split.arrangedSubviews[1].frame.width, 740, accuracy: 1)
    }

    @MainActor
    private func verifySavedSize(
        layout: SplitPaneLayout, defaultLength: Double, position: CGFloat, savedLength: CGFloat
    ) async throws {
        let suite = "EchoSplitTests.\(UUID().uuidString)"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: suite))
        defer { defaults.removePersistentDomain(forName: suite) }
        defaults.register(defaults: ["paneLength": defaultLength])
        let binding = Binding(
            get: { defaults.double(forKey: "paneLength") },
            set: { defaults.set($0, forKey: "paneLength") })
        let root = PersistentSplitView(layout: layout, paneLength: binding) {
            Color.red
        } second: {
            Color.blue
        }
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 1200, height: 800),
            styleMask: [.titled, .resizable], backing: .buffered, defer: false)
        defer { window.orderOut(nil) }
        let firstHost = NSHostingView(rootView: root)
        window.contentView = firstHost
        firstHost.layoutSubtreeIfNeeded()
        try await Task.sleep(for: .milliseconds(100))
        let firstSplit = try XCTUnwrap(findSplit(in: firstHost))
        XCTAssertEqual(paneLength(firstSplit, layout: layout), defaultLength, accuracy: 1)

        firstSplit.setPosition(position, ofDividerAt: 0)
        try await Task.sleep(for: .milliseconds(100))
        XCTAssertEqual(defaults.double(forKey: "paneLength"), savedLength, accuracy: 1)

        let reopenedHost = NSHostingView(rootView: root)
        window.contentView = reopenedHost
        reopenedHost.layoutSubtreeIfNeeded()
        try await Task.sleep(for: .milliseconds(100))
        let reopenedSplit = try XCTUnwrap(findSplit(in: reopenedHost))
        XCTAssertEqual(paneLength(reopenedSplit, layout: layout), savedLength, accuracy: 1)
        window.setContentSize(NSSize(width: 1600, height: 1000))
        reopenedHost.layoutSubtreeIfNeeded()
        try await Task.sleep(for: .milliseconds(100))
        XCTAssertEqual(paneLength(reopenedSplit, layout: layout), savedLength, accuracy: 1)
        XCTAssertEqual(defaults.double(forKey: "paneLength"), savedLength, accuracy: 1)
    }

    @MainActor
    private func makeSplit(layout: SplitPaneLayout, coordinator: PersistentSplitCoordinator) -> NSSplitView {
        let split = NSSplitView()
        split.isVertical = layout.isVertical
        split.dividerStyle = .thin
        split.addArrangedSubview(NSView())
        split.addArrangedSubview(NSView())
        split.delegate = coordinator
        split.setFrameSize(NSSize(width: 1200, height: 800))
        return split
    }

    @MainActor
    private func paneLength(_ split: NSSplitView, layout: SplitPaneLayout) -> CGFloat {
        let frame = split.arrangedSubviews[layout.paneIndex].frame
        return layout.isVertical ? frame.width : frame.height
    }

    @MainActor
    private func findSplit(in view: NSView) -> NSSplitView? {
        if let split = view as? NSSplitView { return split }
        for child in view.subviews {
            if let split = findSplit(in: child) { return split }
        }
        return nil
    }
}
