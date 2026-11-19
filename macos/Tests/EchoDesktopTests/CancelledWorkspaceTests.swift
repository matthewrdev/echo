import Foundation
import XCTest

@testable import EchoDesktop

final class CancelledWorkspaceTests: XCTestCase {
    @MainActor
    func testCancellationRefreshAndReopeningKeepCompletedResultsButHistoryStaysExact() async throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString + ".echo")
        defer { try? FileManager.default.removeItem(at: folder) }
        for stage in ["alignment", "cloud"] {
            let artifact = folder.appendingPathComponent("artifacts/\(stage)/saved")
            try FileManager.default.createDirectory(at: artifact, withIntermediateDirectories: true)
            try Data("{}".utf8).write(to: artifact.appendingPathComponent("manifest.json"))
        }
        let cli = TestSavedArtifactCLIClient()
        let suite = "EchoTests.\(UUID().uuidString)"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: suite))
        defer { defaults.removePersistentDomain(forName: suite) }
        let store = UserDefaultsRecentProjectStore(defaults: defaults)
        let workspace = workspace(cli: cli, root: folder.path, store: store)
        await workspace.reload()
        XCTAssertEqual(workspace.stageStatus(.cloud), "Saved · awaiting validation")
        XCTAssertEqual(
            workspace.destination, .artifact(.cloud), "Open the latest available result, not the unfinished target")
        XCTAssertEqual(cli.requestedRuns.count, 6, "Repeated cancellations must not hide the last completed run")

        workspace.destination = .images
        cli.status = "cancelled"
        await workspace.reload()
        XCTAssertEqual(workspace.destination, .images, "Polling must preserve the user's navigation")
        XCTAssertEqual(workspace.stageStatus(.cloud), "Saved from earlier run")
        XCTAssertTrue(workspace.progressMessage.contains("Saved results are still available"))
        XCTAssertTrue(workspace.progressMessage.contains("repeats any unfinished steps"))
        workspace.start(through: .fusion)
        for _ in 0..<200 where workspace.busy {
            try await Task.sleep(for: .milliseconds(10))
        }
        XCTAssertFalse(workspace.busy)
        XCTAssertNil(workspace.errorMessage, "Normal cancellation must not present an error alert")
        XCTAssertEqual(workspace.geographicRun?.id, "saved", "Map exports must refer to the run owning the result")
        XCTAssertTrue(try XCTUnwrap(workspace.inspection?.run).artifacts.isEmpty)
        XCTAssertEqual(cli.requestedRuns.count, 6, "Polling must reuse loaded run metadata")

        let reopened = self.workspace(cli: cli, root: folder.path, store: store)
        await reopened.reload()
        XCTAssertEqual(reopened.stageStatus(.cloud), "Saved from earlier run")
        XCTAssertEqual(reopened.destination, .artifact(.cloud))
        reopened.selectedRun = "current"
        await reopened.reload()
        XCTAssertTrue(try XCTUnwrap(reopened.displayedRun).artifacts.isEmpty, "History shows this attempt only")
        reopened.selectedRun = "latest"
        await reopened.reload()
        XCTAssertEqual(reopened.retainedStages, [.alignment, .cloud])

        try FileManager.default.removeItem(at: folder.appendingPathComponent("artifacts/cloud/saved"))
        await reopened.reload()
        XCTAssertFalse(reopened.retainedStages.contains(.cloud), "Deleted artifacts must not reappear")
    }

    @MainActor
    func testOpeningUsesPartialResultsAndReevaluatesWhenAnotherProjectOpens() async throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString + ".echo")
        let cli = TestSavedArtifactCLIClient()
        cli.status = "cancelled"
        cli.currentArtifacts = ["features": "features/saved", "alignmentPreview": "alignment/partial"]
        let suite = "EchoTests.\(UUID().uuidString)"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: suite))
        defer { defaults.removePersistentDomain(forName: suite) }
        let store = UserDefaultsRecentProjectStore(defaults: defaults)
        let workspace = workspace(cli: cli, root: folder.path, store: store)
        await workspace.reload()
        XCTAssertEqual(workspace.destination, .artifact(.alignment))

        workspace.destination = .images
        cli.currentArtifacts["export"] = "export/saved"
        await workspace.reload()
        XCTAssertEqual(workspace.destination, .images, "New results must not redirect an already open workspace")

        let recent = try XCTUnwrap(
            store.record(path: folder.appendingPathComponent("another.echo").path, name: "Another").first)
        workspace.openRecent(recent)
        for _ in 0..<200 where workspace.openingProject {
            try await Task.sleep(for: .milliseconds(10))
        }
        XCTAssertFalse(workspace.openingProject)
        XCTAssertEqual(workspace.destination, .artifact(.export), "Opening another project selects its latest output")

        workspace.showProjects()
        cli.currentArtifacts = [:]
        workspace.openRecent(recent)
        for _ in 0..<200 where workspace.openingProject {
            try await Task.sleep(for: .milliseconds(10))
        }
        XCTAssertFalse(workspace.openingProject)
        XCTAssertEqual(workspace.destination, .images, "Unprocessed projects open their photographs")
    }

    @MainActor
    private func workspace(cli: CLIClient, root: String, store: RecentProjectStore) -> WorkspaceViewModel {
        WorkspaceViewModel(
            client: CLIProjectClient(cli: cli), project: root,
            logReader: RunLogReader(formatter: RunEventFormatter()), eventFormatter: RunEventFormatter(),
            recentProjectStore: store, clipboard: SystemTextClipboard())
    }
}
