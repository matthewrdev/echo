import Foundation
import XCTest

@testable import EchoDesktop

final class ProjectManagementTests: XCTestCase {
    @MainActor
    func testDeletionReadsOnlyProjectIdentityEvenWhenArtifactsCannotBeRead() async throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString + ".echo")
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder) }
        let cli = TestProjectManagementCLIClient()
        try cli.response.write(to: folder.appendingPathComponent("project.bin"))
        let suite = "EchoTests.\(UUID().uuidString)"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: suite))
        defer { defaults.removePersistentDomain(forName: suite) }
        let store = UserDefaultsRecentProjectStore(defaults: defaults)
        let recent = try XCTUnwrap(store.record(path: folder.path, name: "Saved project").first)
        let workspace = workspace(cli: cli, store: store, project: "")

        let project = try await workspace.projectForDeletion(recent)

        XCTAssertEqual(project?.id, "saved-project")
        XCTAssertEqual(cli.requests, [["--project", recent.path, "project", "info"]])
        XCTAssertTrue(FileManager.default.fileExists(atPath: folder.appendingPathComponent("project.bin").path))
        XCTAssertEqual(workspace.recentProjects.count, 1, "Preparing confirmation must not delete the project")
    }

    @MainActor
    func testDeletingAnAlreadyMissingFolderRemovesTheStaleEntryAndFailedOpenState() async throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString + ".echo")
        let cli = TestProjectManagementCLIClient()
        let suite = "EchoTests.\(UUID().uuidString)"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: suite))
        defer { defaults.removePersistentDomain(forName: suite) }
        let store = UserDefaultsRecentProjectStore(defaults: defaults)
        let recent = try XCTUnwrap(store.record(path: folder.path, name: "Missing project").first)
        let workspace = workspace(cli: cli, store: store, project: recent.path)
        workspace.openingError = "Not an Echo project"
        workspace.busy = true

        let project = try await workspace.projectForDeletion(recent)

        XCTAssertNil(project)
        XCTAssertTrue(workspace.recentProjects.isEmpty)
        XCTAssertTrue(store.load().isEmpty)
        XCTAssertTrue(workspace.root.isEmpty)
        XCTAssertNil(workspace.openingError)
        XCTAssertTrue(cli.requests.isEmpty)
        XCTAssertFalse(FileManager.default.fileExists(atPath: folder.path))
    }

    @MainActor
    func testFolderWithoutManifestIsPreservedWithRecoveryInstructions() async throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString + ".echo")
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder) }
        let retained = folder.appendingPathComponent("keep.txt")
        try Data("saved files".utf8).write(to: retained)
        let cli = TestProjectManagementCLIClient()
        let suite = "EchoTests.\(UUID().uuidString)"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: suite))
        defer { defaults.removePersistentDomain(forName: suite) }
        let store = UserDefaultsRecentProjectStore(defaults: defaults)
        let recent = try XCTUnwrap(store.record(path: folder.path, name: "Old folder").first)
        let workspace = workspace(cli: cli, store: store, project: "")

        do {
            _ = try await workspace.projectForDeletion(recent)
            XCTFail("An unidentified folder must not be offered for project deletion")
        } catch {
            XCTAssertTrue(error.localizedDescription.contains("Finder"))
        }

        XCTAssertEqual(try Data(contentsOf: retained), Data("saved files".utf8))
        XCTAssertEqual(workspace.recentProjects.count, 1)
        XCTAssertTrue(cli.requests.isEmpty)
    }

    @MainActor
    private func workspace(cli: CLIClient, store: RecentProjectStore, project: String) -> WorkspaceViewModel {
        WorkspaceViewModel(
            client: CLIProjectClient(cli: cli), project: project,
            logReader: RunLogReader(formatter: RunEventFormatter()), eventFormatter: RunEventFormatter(),
            recentProjectStore: store, clipboard: SystemTextClipboard())
    }
}
