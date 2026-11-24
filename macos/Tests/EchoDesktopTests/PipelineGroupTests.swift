import AppKit
import SwiftUI
import XCTest

@testable import EchoDesktop

final class PipelineGroupTests: XCTestCase {
    func testGroupsCoverPipelineOnceInDependencyOrderAndExportIsSeparate() {
        XCTAssertEqual(PipelineGroup.allCases.flatMap(\.stages), PipelineStage.allCases)
        XCTAssertEqual(PipelineGroup.images.target, .alignment)
        XCTAssertEqual(PipelineGroup.pointCloud.target, .cloud)
        XCTAssertEqual(PipelineGroup.meshing.target, .mesh)
        XCTAssertEqual(PipelineGroup.texturing.stages, [.texture])
        XCTAssertEqual(PipelineGroup.export.stages, [.export])
        XCTAssertFalse(PipelineGroup.texturing.isExpandable)
        XCTAssertFalse(PipelineGroup.export.isExpandable)
        XCTAssertTrue(PipelineGroup.images.contains(.images))
        XCTAssertFalse(PipelineGroup.export.contains(.artifact(.texture)))
    }

    @MainActor
    func testGroupRunsIssueOneCommandThroughTheTargetAndPreventOverlappingRuns() async throws {
        let cli = TestPipelineGroupCLIClient()
        let workspace = try workspace(cli: cli)
        await workspace.reload()
        for group in PipelineGroup.allCases {
            let previousCount = cli.starts.count
            workspace.selectedRun = "historical-run"
            workspace.start(group: group)
            XCTAssertEqual(workspace.selectedRun, "latest")
            XCTAssertFalse(workspace.canRunGroups)
            workspace.start(group: .export)
            for _ in 0..<200 where workspace.busy {
                try await Task.sleep(for: .milliseconds(10))
            }
            XCTAssertFalse(workspace.busy)
            XCTAssertNil(workspace.errorMessage)
            XCTAssertEqual(cli.starts.count, previousCount + 1)
            XCTAssertEqual(cli.starts.last, ["run", "start", "--target", group.target.rawValue, "--events", "none"])
        }
        workspace.runs = [RunSummary(id: "active", status: "running", target: "export", created: "")]
        XCTAssertFalse(workspace.canRunGroups)
        workspace.start(group: .export)
        XCTAssertEqual(cli.starts.count, PipelineGroup.allCases.count)
    }

    @MainActor
    func testGroupRunsRequireTwoEnabledPhotographs() async throws {
        let cli = TestPipelineGroupCLIClient()
        let workspace = try workspace(cli: cli)
        XCTAssertFalse(workspace.canRunGroups)
        cli.enabledImages = 1
        await workspace.reload()
        XCTAssertFalse(workspace.canRunGroups)
        workspace.start(group: .images)
        XCTAssertTrue(cli.starts.isEmpty)
        cli.enabledImages = 2
        await workspace.reload()
        XCTAssertTrue(workspace.canRunGroups)
    }

    @MainActor
    func testRenderGroupedSidebar() async throws {
        guard let path = ProcessInfo.processInfo.environment["ECHO_TEST_GROUP_RENDER"] else {
            throw XCTSkip("Set ECHO_TEST_GROUP_RENDER to capture the grouped sidebar")
        }
        let workspace = try workspace(cli: TestPipelineGroupCLIClient())
        await workspace.reload()
        workspace.destination = .artifact(.mesh)
        let view = NSHostingView(rootView: ProjectSidebar(workspace: workspace))
        let window = NSWindow(
            contentRect: CGRect(x: 0, y: 0, width: 300, height: 900), styleMask: [.borderless],
            backing: .buffered, defer: false)
        window.contentView = view
        window.orderFront(nil)
        defer { window.orderOut(nil) }
        try await Task.sleep(for: .milliseconds(250))
        view.layoutSubtreeIfNeeded()
        let bitmap = try XCTUnwrap(view.bitmapImageRepForCachingDisplay(in: view.bounds))
        view.cacheDisplay(in: view.bounds, to: bitmap)
        try XCTUnwrap(bitmap.representation(using: .png, properties: [:])).write(to: URL(fileURLWithPath: path))
    }

    @MainActor
    private func workspace(cli: CLIClient) throws -> WorkspaceViewModel {
        let suite = "EchoTests.\(UUID().uuidString)"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: suite))
        addTeardownBlock { defaults.removePersistentDomain(forName: suite) }
        return WorkspaceViewModel(
            client: CLIProjectClient(cli: cli), project: "/tmp/echo-pipeline-group-test.echo",
            logReader: RunLogReader(formatter: RunEventFormatter()), eventFormatter: RunEventFormatter(),
            recentProjectStore: UserDefaultsRecentProjectStore(defaults: defaults),
            clipboard: SystemTextClipboard())
    }
}
