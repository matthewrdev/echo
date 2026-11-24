import AppKit
import SwiftUI
import XCTest

@testable import EchoDesktop

final class SavedDepthStageQualityTests: XCTestCase {
    @MainActor
    func testSavedDepthOverallGradeAndSidebarAtMinimumWidth() async throws {
        try WindowedTests.requireOptIn()

        let environment = ProcessInfo.processInfo.environment
        guard let path = environment["ECHO_TEST_UI_PROJECT"], let runID = environment["ECHO_TEST_UI_RUN"],
            let cli = environment["ECHO_TEST_CLI"], let output = environment["ECHO_TEST_UI_RENDER"]
        else { throw XCTSkip("Set a saved depth project, run, CLI and render folder to inspect the native sidebar") }
        let domain = "echo-depth-quality-test-\(UUID().uuidString)"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: domain))
        defer { defaults.removePersistentDomain(forName: domain) }
        let workspace = WorkspaceViewModel(
            client: CLIProjectClient(cli: ProcessCLIClient(executable: URL(fileURLWithPath: cli))), project: path,
            logReader: RunLogReader(formatter: RunEventFormatter()), eventFormatter: RunEventFormatter(),
            recentProjectStore: UserDefaultsRecentProjectStore(defaults: defaults), clipboard: SystemTextClipboard())
        workspace.selectedRun = runID
        workspace.selectedStage = .depth
        await workspace.reload()
        let request = try XCTUnwrap(workspace.depthQualityRequest, workspace.status)
        let quality = DepthStageQualityViewModel()
        let start = Date()
        await quality.load(request)
        let summary = try XCTUnwrap(quality.summary, quality.error ?? "No summary")
        XCTAssertGreaterThan(summary.mapCount, 0)
        XCTAssertNotNil(summary.label)
        print("OVERALL DEPTH: \(summary.label ?? "") · \(summary.mapCount) maps · \(Date().timeIntervalSince(start)) s")
        print(summary.help)
        let folder = URL(fileURLWithPath: output)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        for width in [250.0, 310.0] {
            let host = NSHostingView(
                rootView: ArtifactTreeView(workspace: workspace, depthQuality: quality)
                    .environment(\.colorScheme, .light).background(Color.white))
            host.frame = CGRect(x: 0, y: 0, width: width, height: 800)
            let window = NSWindow(contentRect: host.frame, styleMask: [.borderless], backing: .buffered, defer: false)
            window.contentView = host
            host.layoutSubtreeIfNeeded()
            try await Task.sleep(for: .milliseconds(300))
            host.layoutSubtreeIfNeeded()
            let bitmap = try XCTUnwrap(host.bitmapImageRepForCachingDisplay(in: host.bounds))
            host.cacheDisplay(in: host.bounds, to: bitmap)
            try XCTUnwrap(bitmap.representation(using: .png, properties: [:])).write(
                to: folder.appendingPathComponent("depth-sidebar-\(Int(width)).png"))
            window.contentView = nil
        }
    }
}
