import AppKit
import SwiftUI
import XCTest

@testable import EchoDesktop

final class PerformanceTraceViewerTests: XCTestCase {
    @MainActor
    func testSavedTraceFromProcessorRendersTimelineAndTable() async throws {
        let environment = ProcessInfo.processInfo.environment
        guard let root = environment["ECHO_TEST_TRACE_PROJECT"], let cli = environment["ECHO_TEST_CLI"],
            let output = environment["ECHO_TEST_RENDER_DIR"]
        else { throw XCTSkip("Set a traced project and processor to inspect the native performance viewer") }
        let client = CLIProjectClient(cli: ProcessCLIClient(executable: URL(fileURLWithPath: cli)))
        let domain = "echo-performance-test-\(UUID().uuidString)"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: domain))
        defer { defaults.removePersistentDomain(forName: domain) }
        let workspace = WorkspaceViewModel(
            client: client, project: root,
            logReader: RunLogReader(formatter: RunEventFormatter()), eventFormatter: RunEventFormatter(),
            recentProjectStore: UserDefaultsRecentProjectStore(defaults: defaults), clipboard: SystemTextClipboard())
        await workspace.performance.refresh(root: root, preferredRun: nil)
        XCTAssertNil(workspace.performance.error)
        let document = try XCTUnwrap(workspace.performance.document)
        XCTAssertFalse(document.spans.isEmpty)
        XCTAssertTrue(document.spans.contains { $0.kind == .stage })
        XCTAssertTrue(document.statistics.contains { $0.kind == .stage })
        XCTAssertGreaterThan(workspace.performance.attempts.count, 1)
        workspace.performance.comparison = workspace.performance.attempts.last!.id
        await workspace.performance.load(comparing: true)
        XCTAssertNotNil(workspace.performance.baseline)
        let view = NSHostingView(
            rootView: PerformanceTraceView(workspace: workspace, model: workspace.performance)
                .environment(\.colorScheme, .light).frame(width: 1100, height: 680).background(Color.white))
        view.frame = NSRect(x: 0, y: 0, width: 1100, height: 680)
        let window = NSWindow(
            contentRect: view.frame, styleMask: [.borderless], backing: .buffered, defer: false)
        window.contentView = view
        defer { window.contentView = nil }
        view.layoutSubtreeIfNeeded()
        try await Task.sleep(for: .milliseconds(600))
        view.layoutSubtreeIfNeeded()
        let bitmap = try XCTUnwrap(view.bitmapImageRepForCachingDisplay(in: view.bounds))
        view.cacheDisplay(in: view.bounds, to: bitmap)
        try FileManager.default.createDirectory(atPath: output, withIntermediateDirectories: true)
        try XCTUnwrap(bitmap.representation(using: .png, properties: [:])).write(
            to: URL(fileURLWithPath: output).appendingPathComponent("performance-trace.png"))
    }
}
