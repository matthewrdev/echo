import Foundation
import XCTest

@testable import EchoDesktop

final class WorkspacePollFilesTests: XCTestCase {
    func testProgressPreviewAndImportChangesLeaveSavedRunIdentityIntact() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        let runFolder = root.appendingPathComponent("runs/sample")
        let preview = runFolder.appendingPathComponent("previews/depth")
        try FileManager.default.createDirectory(at: preview, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }

        try Data("{}".utf8).write(to: root.appendingPathComponent("project.bin"))
        try Data("{}".utf8).write(to: runFolder.appendingPathComponent("run.json"))
        let before = try XCTUnwrap(WorkspacePollFiles.read(root: root.path, selectedRun: "sample", activeRun: "sample"))
        XCTAssertEqual(WorkspacePollFiles.read(root: root.path, selectedRun: "sample", activeRun: "sample"), before)

        try Data("{}".utf8).write(to: runFolder.appendingPathComponent("progress.bin"))
        try Data([1]).write(to: preview.appendingPathComponent("units.bin"))
        try Data([1]).write(to: runFolder.appendingPathComponent("events.bin"))
        try Data([1]).write(to: root.appendingPathComponent("project.import.bin"))
        let during = try XCTUnwrap(WorkspacePollFiles.read(root: root.path, selectedRun: "sample", activeRun: "sample"))
        XCTAssertEqual(during.project, before.project)
        XCTAssertEqual(during.runs, before.runs)
        XCTAssertNotEqual(during.progress, before.progress)
        XCTAssertNotEqual(during.previews, before.previews)
        XCTAssertNotEqual(during.events, before.events)
        XCTAssertNotEqual(during.imports, before.imports)
    }
}
