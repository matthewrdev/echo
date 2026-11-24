import AppKit
import SwiftUI
import XCTest

@testable import EchoDesktop

final class ArtifactStatusIconTests: XCTestCase {
    @MainActor
    func testOnlyActiveStatusMovesAndReduceMotionKeepsItStill() async throws {
        try WindowedTests.requireOptIn()

        for isProcessing in [true, false] {
            let view = NSHostingView(
                rootView: ArtifactStatusIcon(
                    symbol: isProcessing ? "arrow.trianglehead.2.clockwise.rotate.90" : "checkmark.circle.fill",
                    color: .blue, isProcessing: isProcessing
                )
                .frame(width: 60, height: 60).background(Color.white))
            let window = NSWindow(
                contentRect: CGRect(x: 0, y: 0, width: 60, height: 60), styleMask: [.borderless],
                backing: .buffered, defer: false)
            window.contentView = view
            window.orderFront(nil)
            defer { window.orderOut(nil) }
            try await Task.sleep(for: .milliseconds(120))
            let first = try snapshot(view)
            try await Task.sleep(for: .milliseconds(250))
            let second = try snapshot(view)
            if isProcessing && !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion {
                XCTAssertNotEqual(first, second, "The processing status should visibly move between updates")
            } else {
                XCTAssertEqual(first, second, "Completed status and Reduce Motion must remain still")
            }
        }
    }

    @MainActor
    private func snapshot(_ view: NSView) throws -> Data {
        view.layoutSubtreeIfNeeded()
        let bitmap = try XCTUnwrap(view.bitmapImageRepForCachingDisplay(in: view.bounds))
        view.cacheDisplay(in: view.bounds, to: bitmap)
        return try XCTUnwrap(bitmap.representation(using: .png, properties: [:]))
    }
}
