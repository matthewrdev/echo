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
        XCTAssertEqual(cli.requestedRuns.count, 6, "Repeated cancellations must not hide the last completed run")

        cli.status = "cancelled"
        await workspace.reload()
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
    private func workspace(cli: CLIClient, root: String, store: RecentProjectStore) -> WorkspaceViewModel {
        WorkspaceViewModel(
            client: CLIProjectClient(cli: cli), project: root,
            logReader: RunLogReader(formatter: RunEventFormatter()), eventFormatter: RunEventFormatter(),
            recentProjectStore: store, clipboard: SystemTextClipboard())
    }
}
