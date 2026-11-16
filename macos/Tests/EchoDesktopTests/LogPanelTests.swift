import AppKit
import SwiftUI
import XCTest

@testable import EchoDesktop

final class LogPanelTests: XCTestCase {
    @MainActor
    func testUserScrollPausesFollowingWhileLayoutChangesDoNot() async throws {
        let scroll = NSScrollView(frame: NSRect(x: 0, y: 0, width: 300, height: 100))
        scroll.documentView = NSView(frame: NSRect(x: 0, y: 0, width: 300, height: 2000))
        scroll.contentView.scroll(to: NSPoint(x: 0, y: 1000))
        let paused = expectation(description: "Scrolling into history pauses following")
        let coordinator = LogScrollPositionCoordinator { follows in
            XCTAssertFalse(follows)
            paused.fulfill()
        }
        coordinator.scrollChanged(in: scroll, event: nil)
        coordinator.scrollChanged(in: scroll, event: .scrollWheel)
        await fulfillment(of: [paused], timeout: 1)

        let resumed = expectation(description: "Scrolling to the end resumes following")
        coordinator.update = { follows in
            XCTAssertTrue(follows)
            resumed.fulfill()
        }
        scroll.contentView.scroll(to: .zero)
        coordinator.scrollChanged(in: scroll, event: .leftMouseDragged)
        await fulfillment(of: [resumed], timeout: 1)
    }

    @MainActor
    func testRenderLogPanelAgainstSavedProject() async throws {
        let environment = ProcessInfo.processInfo.environment
        guard let root = environment["ECHO_TEST_LOG_PROJECT"], let cliPath = environment["ECHO_TEST_CLI"],
            let output = environment["ECHO_TEST_LOG_RENDER"]
        else { throw XCTSkip("Set the log project, CLI and render path to inspect the native activity panel") }
        let log = CLICommandLog()
        let client = CLIProjectClient(cli: ProcessCLIClient(executable: URL(fileURLWithPath: cliPath), commandLog: log))
        let domain = "echo-log-panel-test-\(UUID().uuidString)"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: domain))
        defer { defaults.removePersistentDomain(forName: domain) }
        let workspace = WorkspaceViewModel(
            client: client, project: root, logReader: RunLogReader(formatter: RunEventFormatter()),
            eventFormatter: RunEventFormatter(), recentProjectStore: UserDefaultsRecentProjectStore(defaults: defaults),
            clipboard: SystemTextClipboard(), commandLog: log)
        workspace.selectedStage = .mesh
        _ = try await client.storage(root: root)
        await workspace.reload()
        XCTAssertGreaterThan(workspace.logs.filter { $0.stage == "mesh" }.count, 60)
        XCTAssertTrue(workspace.commands.contains { !$0.isBackgroundQuery })
        let view = NSHostingView(
            rootView:
                StageLogView(workspace: workspace, expanded: .constant(true)).environment(\.colorScheme, .light)
                .frame(width: 1100, height: 350).background(Color.white))
        view.frame = NSRect(x: 0, y: 0, width: 1100, height: 350)
        view.layoutSubtreeIfNeeded()
        try await Task.sleep(for: .milliseconds(100))
        let bitmap = try XCTUnwrap(view.bitmapImageRepForCachingDisplay(in: view.bounds))
        view.cacheDisplay(in: view.bounds, to: bitmap)
        try XCTUnwrap(bitmap.representation(using: .png, properties: [:])).write(to: URL(fileURLWithPath: output))
    }
}
