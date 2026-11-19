import AppKit
import SwiftUI
import XCTest

@testable import EchoDesktop

final class PhotoImportProgressTests: XCTestCase {
    @MainActor
    func testStreamedImportCountsOverrideOldProcessingAndClearOnCompletionOrFailure() async throws {
        let cli = TestPhotoImportCLIClient()
        let suite = "EchoImportTests.\(UUID().uuidString)"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: suite))
        defer { defaults.removePersistentDomain(forName: suite) }
        let workspace = WorkspaceViewModel(
            client: CLIProjectClient(cli: cli), project: "/tmp/import-test.echo",
            logReader: RunLogReader(formatter: RunEventFormatter()), eventFormatter: RunEventFormatter(),
            recentProjectStore: UserDefaultsRecentProjectStore(defaults: defaults), clipboard: SystemTextClipboard())
        await workspace.reload()
        workspace.workingRun = RunData(
            id: "old", status: "completed", target: "texture", snapshot: try XCTUnwrap(workspace.inspection?.project),
            artifacts: [:], error: nil,
            progress: RunProgress(
                stage: "texture", message: "Old texture progress", completed: 1, total: 1, updated: nil))
        workspace.importPhotos(from: URL(fileURLWithPath: "/tmp/photos"))
        for _ in 0..<100 where !(await cli.importing) { try await Task.sleep(for: .milliseconds(10)) }
        XCTAssertTrue(workspace.importingPhotos)
        await cli.emit(
            #"{"type":"imageImported","name":"photo.jpg","count":290,"completed":290,"total":540,"skipped":0}"#)
        for _ in 0..<100 where workspace.photoImportProgress?.completed != 290 {
            try await Task.sleep(for: .milliseconds(10))
        }
        XCTAssertEqual(workspace.progressActivity, "Importing photos")
        XCTAssertEqual(workspace.progressDetail, "Imported 290/540")
        XCTAssertTrue(workspace.progressMessage.contains("photo.jpg"))
        XCTAssertEqual(try XCTUnwrap(workspace.photoImportProgress).fractionCompleted, 290.0 / 540)
        XCTAssertFalse(workspace.canEdit)
        if let output = ProcessInfo.processInfo.environment["ECHO_TEST_IMPORT_RENDER"] {
            let host = NSHostingView(
                rootView: VStack(alignment: .leading) {
                    ArtifactTreeView(workspace: workspace).frame(width: 300, height: 430)
                    GlobalProgressView(workspace: workspace)
                }.frame(width: 700).background(Color.white).environment(\.colorScheme, .light))
            let window = NSWindow(
                contentRect: NSRect(x: 0, y: 0, width: 700, height: 530), styleMask: [.borderless], backing: .buffered,
                defer: false)
            window.contentView = host
            defer { window.contentView = nil }
            host.layoutSubtreeIfNeeded()
            let bitmap = try XCTUnwrap(host.bitmapImageRepForCachingDisplay(in: host.bounds))
            host.cacheDisplay(in: host.bounds, to: bitmap)
            try XCTUnwrap(bitmap.representation(using: .png, properties: [:])).write(to: URL(fileURLWithPath: output))
        }
        await cli.emit(
            #"{"type":"imageImportSkipped","name":"duplicate.jpg","count":290,"completed":291,"total":540,"skipped":1}"#
        )
        for _ in 0..<100 where workspace.photoImportProgress?.completed != 291 {
            try await Task.sleep(for: .milliseconds(10))
        }
        XCTAssertEqual(workspace.progressDetail, "Checked 291/540 · 290 imported · 1 already present")
        await cli.emit(
            #"{"type":"imageImported","name":"last.jpg","count":539,"completed":540,"total":540,"skipped":1}"#)
        await cli.finish()
        for _ in 0..<100 where workspace.busy { try await Task.sleep(for: .milliseconds(10)) }
        XCTAssertEqual(workspace.photoImportProgress?.fractionCompleted, 1, "Final buffered counts must be delivered")
        XCTAssertFalse(workspace.importingPhotos)
        XCTAssertNil(workspace.errorMessage)
        workspace.importPhotos(from: URL(fileURLWithPath: "/tmp/photos"))
        XCTAssertNil(workspace.photoImportProgress, "A second import must not reuse the first import's counts")
        for _ in 0..<100 where !(await cli.importing) { try await Task.sleep(for: .milliseconds(10)) }
        await cli.finish(failing: true)
        for _ in 0..<100 where workspace.busy { try await Task.sleep(for: .milliseconds(10)) }
        XCTAssertFalse(workspace.importingPhotos)
        XCTAssertNotNil(workspace.errorMessage)
        XCTAssertEqual(workspace.photoImportProgress?.completed, 0)
    }
}
